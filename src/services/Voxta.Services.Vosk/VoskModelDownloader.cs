﻿using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using ChatMate.Abstractions.Repositories;
using Microsoft.Extensions.Logging;

namespace ChatMate.Services.Vosk;

public interface IVoskModelDownloader
{
    Task<global::Vosk.Model> AcquireModelAsync(CancellationToken cancellationToken);
}

public class VoskModelDownloader : IVoskModelDownloader
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);
    
    private readonly ILogger<VoskModelDownloader> _logger;
    private readonly IProfileRepository _profileRepository;

    public VoskModelDownloader(ILogger<VoskModelDownloader> logger, IProfileRepository profileRepository)
    {
        _logger = logger;
        _profileRepository = profileRepository;
    }
    
    public async Task<global::Vosk.Model> AcquireModelAsync(CancellationToken cancellationToken)
    {
        await Semaphore.WaitAsync(cancellationToken);
        try
        {
            return await AcquireModelInternalAsync(cancellationToken);
        }
        finally
        {
            Semaphore.Release();
        }
    }

    private async Task<global::Vosk.Model> AcquireModelInternalAsync(CancellationToken cancellationToken)
    {

        var profile = await _profileRepository.GetProfileAsync(cancellationToken);
        if (profile == null || string.IsNullOrEmpty(profile.Services.SpeechToText.Model))
            throw new NullReferenceException("There is no Vosk settings in the profile");
        var modelsPath = Path.GetFullPath("Models/Vosk");
        var modelName = profile.Services.SpeechToText.Model;
        var modelZipHash = profile.Services.SpeechToText.Hash;
        var modelPath = Path.Combine(modelsPath, modelName);
        
        if (Directory.Exists(modelPath))
        {
            _logger.LogInformation("Vosk model already downloaded");
            return new global::Vosk.Model(modelPath);
        }
        
        var fileUrl = $"https://alphacephei.com/vosk/models/{modelName}.zip";

        _logger.LogInformation("Downloading Vosk model from {FileUrl}...", fileUrl);
        using var httpClient = new HttpClient();
        var fileBytes = await httpClient.GetByteArrayAsync(fileUrl, cancellationToken);
        var hashBytes = SHA256.HashData(fileBytes);
        var actualZipHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        _logger.LogInformation("Downloaded Vosk model, hash is {ActualZipHash}", actualZipHash);
        if (!string.IsNullOrEmpty(modelZipHash) && actualZipHash != modelZipHash)
            throw new SecurityException($"Expected vosk model to have hash '{modelZipHash}' but hash was '{actualZipHash}'.");
        Directory.CreateDirectory("Models/Vosk");
        _logger.LogInformation("Extracting Vosk model...");
        using var stream = new MemoryStream(fileBytes);
        using var archive = new ZipArchive(stream);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            var entryExtractPath = Path.Combine(modelsPath, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(entryExtractPath)!);
            entry.ExtractToFile(entryExtractPath, overwrite: true);
        }
        _logger.LogInformation("Extracted Vosk model");
        if (!Directory.Exists(modelPath))
            throw new Exception("Vosk model directory does not exist after extracting");
        return new global::Vosk.Model(modelPath);
    }
}