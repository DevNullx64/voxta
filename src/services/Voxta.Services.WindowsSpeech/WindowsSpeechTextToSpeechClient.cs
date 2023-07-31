﻿#if(WINDOWS)
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Speech.Synthesis;
using Voxta.Abstractions.Diagnostics;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Network;
using Voxta.Abstractions.Repositories;
using Voxta.Abstractions.Services;
using Voxta.Shared.TextToSpeechUtils;

namespace Voxta.Services.WindowsSpeech;

public class WindowsSpeechTextToSpeechClient : ITextToSpeechService
{
    public string ServiceName => WindowsSpeechConstants.ServiceName;
    public string[] Features => new[] { ServiceFeatures.NSFW };

    private readonly ISettingsRepository _settingsRepository;
    private readonly IPerformanceMetrics _performanceMetrics;
    private readonly ITextToSpeechPreprocessor _preprocessor;
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private string[]? _thinkingSpeech;
    private readonly SpeechSynthesizer _synthesizer;

    public WindowsSpeechTextToSpeechClient(ISettingsRepository settingsRepository, IPerformanceMetrics performanceMetrics, ITextToSpeechPreprocessor preprocessor)
    {
        _settingsRepository = settingsRepository;
        _performanceMetrics = performanceMetrics;
        _preprocessor = preprocessor;
        
        _synthesizer = new SpeechSynthesizer();
    }
    
    public async Task<bool> InitializeAsync(string[] prerequisites, string culture, CancellationToken cancellationToken)
    {
        var settings = await _settingsRepository.GetAsync<WindowsSpeechSettings>(cancellationToken);
        if (settings == null) return false;
        if (!settings.Enabled) return false;
        _culture = CultureInfo.GetCultureInfo(culture);
        _thinkingSpeech = settings.ThinkingSpeech;
        return true;
    }

    public string ContentType => "audio/x-wav";

    public string[] GetThinkingSpeech()
    {
        return _thinkingSpeech ?? Array.Empty<string>();
    }

    public Task<Abstractions.Model.VoiceInfo[]> GetVoicesAsync(CancellationToken cancellationToken)
    {
        var voices = _synthesizer.GetInstalledVoices(_culture)
            .Where(voice => voice.Enabled)
            .Select(v => new Abstractions.Model.VoiceInfo
            {
                Id = v.VoiceInfo.Name,
                Label = $"{v.VoiceInfo.Name} ({v.VoiceInfo.Gender})"
            })
            .ToArray();
        return Task.FromResult(voices);
    }

    public async Task GenerateSpeechAsync(SpeechRequest speechRequest, ISpeechTunnel tunnel, CancellationToken cancellationToken)
    {
#if WINDOWS
        var ttsPerf = _performanceMetrics.Start("WindowsSpeech.TextToSpeech");
        _synthesizer.SelectVoice(GetVoice(speechRequest));
        var stream = new MemoryStream();
        _synthesizer.SetOutputToWaveStream(stream);
        _synthesizer.Speak(_preprocessor.Preprocess(speechRequest.Text, _culture.Name));
        await tunnel.SendAsync(new AudioData(stream, "audio/x-wav"), cancellationToken);
        ttsPerf.Done();
#else
        throw new PlatformNotSupportedException("This function is only supported on Windows.");
#endif
    }

    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    private string GetVoice(SpeechRequest speechRequest)
    {
        if (!string.IsNullOrEmpty(speechRequest.Voice) && speechRequest.Voice != SpecialVoices.Male && speechRequest.Voice != SpecialVoices.Female)
            return speechRequest.Voice;

        var voices = _synthesizer.GetInstalledVoices(_culture);
        InstalledVoice? voice = null;
        if (string.IsNullOrEmpty(speechRequest.Voice))
            voice = voices.FirstOrDefault(v => v.VoiceInfo.Gender == VoiceGender.Neutral) ?? voices.FirstOrDefault();
        else if (speechRequest.Voice == SpecialVoices.Female)
            voice = voices.FirstOrDefault(v => v.VoiceInfo.Gender == VoiceGender.Female);
        else if (speechRequest.Voice == SpecialVoices.Male)
            voice = voices.FirstOrDefault(v => v.VoiceInfo.Gender == VoiceGender.Male);

        if (voice != null) return voice.VoiceInfo.Name;
        voice = voices.FirstOrDefault();
        if (voice == null) throw new WindowsSpeechException("Could not find any installed voices");
        return voice.VoiceInfo.Name;
    }

    public void Dispose()
    {
    }
}
#endif
