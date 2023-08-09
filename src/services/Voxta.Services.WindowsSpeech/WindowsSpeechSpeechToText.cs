﻿#if(WINDOWS)
using System.Speech.Recognition;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Repositories;
using Voxta.Abstractions.Services;

namespace Voxta.Services.WindowsSpeech;

public class WindowsSpeechSpeechToText : ISpeechToTextService
{
    private ManualResetEventSlim _recognitionStopped = new ManualResetEventSlim(true);

    public string ServiceName => WindowsSpeechConstants.ServiceName;
    public string[] Features { get; } = Array.Empty<string>();
    
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<WindowsSpeechSpeechToText> _logger;
    private SpeechRecognitionEngine ? _recognizer;
    private bool _speaking;

    public event EventHandler? SpeechRecognitionStarted;
    public event EventHandler<string>? SpeechRecognitionPartial;
    public event EventHandler<string>? SpeechRecognitionFinished;

    public WindowsSpeechSpeechToText(ISettingsRepository settingsRepository, ILoggerFactory loggerFactory)
    {
        _settingsRepository = settingsRepository;
        _logger = loggerFactory.CreateLogger<WindowsSpeechSpeechToText>();
    }
    
    public async Task<bool> TryInitializeAsync(string[] prerequisites, string culture, bool dry, CancellationToken cancellationToken)
    {
        var settings = await _settingsRepository.GetAsync<WindowsSpeechSettings>(cancellationToken);
        if (settings == null) return false;
        if (!settings.Enabled) return false;
        if (prerequisites.Contains(ServiceFeatures.NSFW)) return false;
        if (dry) return true;
        
        _recognizer = new SpeechRecognitionEngine();
        var grammar = new DictationGrammar();
        _recognizer.LoadGrammar(grammar);
        _recognizer.SetInputToDefaultAudioDevice();

        _recognizer.SpeechDetected += (_, _) =>
        {
            _logger.LogDebug("Speech detected");
            if (_speaking) return;
            _speaking = true;
            SpeechRecognitionStarted?.Invoke(this, EventArgs.Empty);
        };
        
        _recognizer.SpeechHypothesized += (_, e) =>
        {
            _logger.LogDebug("Speech recognizing");
            SpeechRecognitionPartial?.Invoke(this, e.Result.Text);
        };

        _recognizer.SpeechRecognized += (_, e) =>
        {
            _logger.LogDebug("Speech recognized");
            _speaking = false;
            if (!string.IsNullOrEmpty(e.Result.Text))
                SpeechRecognitionFinished?.Invoke(this, e.Result.Text);
        };

        _recognizer.SpeechRecognitionRejected += (_, _) => {
            _speaking = false;
        };

        _recognizer.RecognizeCompleted += (_, _) =>
        {
            _recognitionStopped.Set();
        };

        _recognizer.RecognizeAsync(RecognizeMode.Multiple);
        return true;
    }

    public void StartMicrophoneTranscription()
    {
        _recognitionStopped.Wait(); // Wait until the recognizer is stopped
        _recognitionStopped.Reset(); // Reset the event
        _recognizer?.RecognizeAsync(RecognizeMode.Multiple);
    }
    
    public void StopMicrophoneTranscription()
    {
        _recognizer?.RecognizeAsyncStop();
    }
    
    public void Dispose()
    {
        _recognizer?.RecognizeAsyncStop();
        _recognitionStopped.Wait();
        _recognizer?.Dispose();
        _recognizer = null;
        _recognitionStopped.Dispose();
    }
}
#endif
