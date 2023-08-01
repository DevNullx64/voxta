﻿using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoMapper;
using Voxta.Abstractions.Diagnostics;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Repositories;
using Voxta.Abstractions.Services;
using Voxta.Common;
using Voxta.Services.OpenSourceLargeLanguageModels;

namespace Voxta.Services.NovelAI;

public class NovelAITextGenService : ITextGenService
{
    private static readonly IMapper Mapper;
    
    static NovelAITextGenService()
    {
        var config = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<NovelAIParameters, NovelAIRequestBodyParameters>();
        });
        Mapper = config.CreateMapper();
    }
    
    public string ServiceName => NovelAIConstants.ServiceName;
    public string[] Features => new[] { ServiceFeatures.NSFW };
    
    private readonly HttpClient _httpClient;
    private NovelAIParameters? _parameters;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IPerformanceMetrics _performanceMetrics;
    private string _model = "clio-v1";
    
    public NovelAITextGenService(ISettingsRepository settingsRepository, IHttpClientFactory httpClientFactory, IPerformanceMetrics performanceMetrics)
    {
        _settingsRepository = settingsRepository;
        _performanceMetrics = performanceMetrics;
        _httpClient = httpClientFactory.CreateClient($"{NovelAIConstants.ServiceName}.TextGen");
    }
    
    public async Task<bool> InitializeAsync(string[] prerequisites, string culture, CancellationToken cancellationToken)
    {
        var settings = await _settingsRepository.GetAsync<NovelAISettings>(cancellationToken);
        if (settings == null) return false;
        if (!settings.Enabled) return false;
        if (string.IsNullOrEmpty(settings.Token)) return false;
        if (!culture.StartsWith("en") && !culture.StartsWith("jp")) return false;
        if (prerequisites.Contains(ServiceFeatures.GPT3)) return false;
        _httpClient.BaseAddress = new Uri("https://api.novelai.net");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Crypto.DecryptString(settings.Token));
        _model = settings.Model;
        _parameters = settings.Parameters ?? NovelAIPresets.DefaultForModel(_model);
        return true;
    }

    public async ValueTask<string> GenerateReplyAsync(IReadOnlyChatSessionData chatSessionData, CancellationToken cancellationToken)
    {
        var builder = new NovelAIPromptBuilder();
        var input = builder.BuildReplyPrompt(chatSessionData, includePostHistoryPrompt: false);
        var parameters = Mapper.Map<NovelAIRequestBodyParameters>(_parameters);

        /*
        // TODO: Add this once I have a NAI tokenizer. Also, most of this can be pre-generate or cached in InitializeAsync.
        var bias = new List<LogitBiasExp>(4)
        {
            new()
            {
                Bias = 2,
                EnsureSequenceFinish = true,
                GenerateOnce = true,
                Sequence = _tokenizer.Encode($"\n{chatSessionData.Character.Name}:")
            },
            new()
            {
                Bias = 0,
                EnsureSequenceFinish = true,
                GenerateOnce = true,
                Sequence = _tokenizer.Encode($"\n{chatSessionData.UserName}: ")
            }
        };
        bias.AddRange(parameters.LogitBiasExp ?? Array.Empty<LogitBiasExp>());
        parameters.LogitBiasExp = bias.ToArray();
        */
        
        var body = new
        {
            model = _model,
            input,
            parameters
        };
        var bodyContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/ai/generate-stream");
        request.Content = bodyContent;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var textGenPerf = _performanceMetrics.Start("NovelAI.TextGen");
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new NovelAIException(await response.Content.ReadAsStringAsync(cancellationToken));

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        var sb = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;
            if (!line.StartsWith("data:")) continue;
            var json = JsonSerializer.Deserialize<NovelAIEventData>(line[5..]);
            if (json == null || json.token.Length == 0) break;
            if (json.token[^1] is '\"' or '\n')
            {
                sb.Append(json.token[..^1]);
                break;
            }
            sb.Append(json.token);
        }
        reader.Close();
        
        textGenPerf.Done();

        var text = sb.ToString();
        
        return text;
    }
    
    public int GetTokenCount(string message)
    {
        return 0;
    }

    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private class NovelAIEventData
    {
        public required string token { get; init; }
        public bool final { get; init; }
        public int ptr { get; init; }
        public string? error { get; init; }
    }

    public void Dispose()
    {
    }
}