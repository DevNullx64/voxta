﻿using Voxta.Abstractions.Model;

namespace Voxta.Abstractions.Services;

public interface ISummarizationService : IService
{
    ValueTask<string> SummarizeAsync(IChatInferenceData chat, CancellationToken cancellationToken);
}