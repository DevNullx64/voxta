﻿using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using Voxta.Abstractions.Diagnostics;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Network;
using Voxta.Abstractions.Repositories;
using Voxta.Abstractions.Services;
using Voxta.Common;
using Microsoft.Extensions.Logging;
using Voxta.Shared.TextToSpeechUtils;

namespace Voxta.Services.ElevenLabs;

public class ElevenLabsTextToSpeechService : ITextToSpeechService
{
    public string ServiceName => ElevenLabsConstants.ServiceName;
    public string[] Features => new[] { ServiceFeatures.NSFW };
    
    private readonly ILogger<ElevenLabsTextToSpeechService> _logger;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IPerformanceMetrics _performanceMetrics;
    private readonly ITextToSpeechPreprocessor _preprocessor;
    private readonly HttpClient _httpClient;
    private string _culture = "en-US";
    private string _model = "eleven_multilingual_v1";
    private ElevenLabsParameters? _parameters;
    private string[]? _thinkingSpeech;

    public ElevenLabsTextToSpeechService(ISettingsRepository settingsRepository, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, IPerformanceMetrics performanceMetrics, ITextToSpeechPreprocessor preprocessor)
    {
        _settingsRepository = settingsRepository;
        _performanceMetrics = performanceMetrics;
        _preprocessor = preprocessor;
        _logger = loggerFactory.CreateLogger<ElevenLabsTextToSpeechService>();
        _httpClient = httpClientFactory.CreateClient($"{ElevenLabsConstants.ServiceName}.TextToSpeech");
    }
    
    public async Task<bool> InitializeAsync(string[] prerequisites, string culture, CancellationToken cancellationToken)
    {
        var settings = await _settingsRepository.GetAsync<ElevenLabsSettings>(cancellationToken);
        if (settings == null) return false;
        if (!settings.Enabled) return false;
        if (string.IsNullOrEmpty(settings.ApiKey)) throw new AuthenticationException("ElevenLabs token is missing.");
        _httpClient.BaseAddress = new Uri("https://api.elevenlabs.io");
        _httpClient.DefaultRequestHeaders.Add("xi-api-key", Crypto.DecryptString(settings.ApiKey));
        _culture = culture;
        _model = settings.Model;
        _parameters = settings.Parameters ?? new ElevenLabsParameters();
        _thinkingSpeech = settings.ThinkingSpeech;
        return true;
    }

    public string ContentType => "audio/mpeg";

    public string[] GetThinkingSpeech()
    {
        return _thinkingSpeech ?? Array.Empty<string>();
    }

    public async Task GenerateSpeechAsync(SpeechRequest speechRequest, ISpeechTunnel tunnel, CancellationToken cancellationToken)
    {
        var voice = GetVoice(speechRequest);
        var body = new
        {
            text = _preprocessor.Preprocess(speechRequest.Text, speechRequest.Culture),
            model_id = _culture == "en-US" ? _model : "eleven_multilingual_v1",
            voice_settings = _parameters
        };
        var ttsPerf = _performanceMetrics.Start("ElevenLabs.TextToSpeech");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/text-to-speech/{voice}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/*"));
        request.Headers.TransferEncodingChunked = false;
        
        var response = await _httpClient.SendAsync(request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var reason = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("ElevenLabs failed to generate speech: {Reason}", reason);
            await tunnel.ErrorAsync(new ElevenLabsException($"Unable to generate speech: {reason}"), cancellationToken);
            return;
        }
        
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await tunnel.SendAsync(new AudioData(stream, response.Content.Headers.ContentType?.MediaType ?? "audio/webm"), cancellationToken);
        ttsPerf.Done();
    }

    private string GetVoice(SpeechRequest speechRequest)
    {
        if (string.IsNullOrEmpty(speechRequest.Voice) || speechRequest.Voice == SpecialVoices.Female)
            return "EXAVITQu4vr4xnSDxMaL"; // Bella
        if (speechRequest.Voice == SpecialVoices.Male)
            return "pNInz6obpgDQGcFmaJgB"; // Adam
        return speechRequest.Voice;
    }

    public async Task<VoiceInfo[]> GetVoicesAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetFromJsonAsync<VoicesResponse>("/v1/voices", cancellationToken);
        if (response == null) throw new NullReferenceException("No voices returned");
        return response.voices.Select(v => new VoiceInfo { Id = v.voice_id, Label = v.name }).ToArray();
    }
    
    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class VoicesResponse
    {
        public required VoiceResponse[] voices { get; init; }
    }
    
    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class VoiceResponse
    {
        public required string voice_id { get; init; }
        public required string name { get; init; }
    }

    public void Dispose()
    {
    }
}