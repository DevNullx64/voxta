﻿using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Repositories;
using Voxta.Abstractions.Services;
using Voxta.Abstractions.System;
using Voxta.Services.AzureSpeechService.Transcribers;

namespace Voxta.Services.AzureSpeechService;

public class AzureSpeechServiceSpeechToText : ISpeechToTextService
{
    public string ServiceName => AzureSpeechServiceConstants.ServiceName;
    public string[] Features { get; private set; } = Array.Empty<string>();
    
    private readonly IRecordingService _recordingService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILocalEncryptionProvider _encryptionProvider;
    private readonly ILogger<AzureSpeechServiceSpeechToText> _logger;
    private IAzureSpeechTranscriber? _transcriber;
    private PushAudioInputStream? _pushStream;

    public event EventHandler? SpeechRecognitionStarted;
    public event EventHandler<string>? SpeechRecognitionPartial;
    public event EventHandler<string>? SpeechRecognitionFinished;

    public AzureSpeechServiceSpeechToText(IRecordingService recordingService, ISettingsRepository settingsRepository, ILoggerFactory loggerFactory, ILocalEncryptionProvider encryptionProvider)
    {
        _recordingService = recordingService;
        _settingsRepository = settingsRepository;
        _encryptionProvider = encryptionProvider;
        _logger = loggerFactory.CreateLogger<AzureSpeechServiceSpeechToText>();
    }
    
    public async Task<bool> TryInitializeAsync(Guid serviceId, string[] prerequisites, string culture, bool dry,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsRepository.GetAsync<AzureSpeechServiceSettings>(TODO, cancellationToken);
        if (settings == null) return false;
        if (!settings.Enabled) return false;
        if (string.IsNullOrEmpty(settings.SubscriptionKey)) return false;
        if (string.IsNullOrEmpty(settings.Region)) return false;
        if (prerequisites.Contains(ServiceFeatures.NSFW) && settings.FilterProfanity) return false;
        if (dry) return true;
        
        var config = SpeechConfig.FromSubscription(_encryptionProvider.Decrypt(settings.SubscriptionKey), settings.Region);
        config.SpeechRecognitionLanguage = culture;
        if (settings.FilterProfanity)
        {
            config.SetProfanity(ProfanityOption.Removed);
        }
        else
        {
            config.SetProfanity(ProfanityOption.Raw);
            Features = new[] { ServiceFeatures.NSFW };
        }
        
        if (!string.IsNullOrEmpty(settings.LogFilename))
        {
            var directory = Path.GetDirectoryName(settings.LogFilename) ?? throw new AzureSpeechServiceException($"Invalid log filename: {settings.LogFilename}");
            Directory.CreateDirectory(directory);
            var filename = DateTimeOffset.Now.ToString("yy-MM-dd") + "_stt_" + Path.GetFileName(settings.LogFilename);
            config.SetProperty(PropertyId.Speech_LogFilename, Path.Combine(directory, filename));
        }
        
        _pushStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
        var audioInput = AudioConfig.FromStreamInput(_pushStream);
        _transcriber = settings.Diarization
            ? new AzureSpeechConversationTranscriber(config, audioInput, _logger)
            : new AzureSpeechRecognizer(config, audioInput, _logger);

        _transcriber.Partial += (_, e) =>
        {            
            _logger.LogDebug("Speech recognizing");
            if (!_recordingService.Speaking)
            {
                _recordingService.Speaking = true;
                SpeechRecognitionStarted?.Invoke(this, EventArgs.Empty);
            }
            SpeechRecognitionPartial?.Invoke(this, e);
        };

        _transcriber.Finished += (_, e) =>
        {
            _logger.LogDebug("Speech recognized");
            _recordingService.Speaking = false;
            if (!string.IsNullOrEmpty(e))
                SpeechRecognitionFinished?.Invoke(this, e);
        };

        _transcriber.Canceled += (_, e) => {
            #warning Here we should notify the UI to stop the loading indicator
            _recordingService.Speaking = false;
        };
        
        _recordingService.DataAvailable += (_, e) =>
        {
            _pushStream?.Write(e.Buffer, e.BytesRecorded);
        };
        
        await _transcriber.StartContinuousRecognitionAsync();
        return true;
    }

    public void StartMicrophoneTranscription()
    {
        _recordingService.StartRecording();
    }
    
    public void StopMicrophoneTranscription()
    {
        _recordingService.StopRecording();
    }
    
    public void Dispose()
    {
        _recordingService.StopRecording();
        #warning Use AsyncDisposable
        _transcriber?.StopContinuousRecognitionAsync().Wait();
        _transcriber?.Dispose();
        _transcriber = null;
        _pushStream?.Close();
        _pushStream?.Dispose();
        _pushStream = null;
    }
}
