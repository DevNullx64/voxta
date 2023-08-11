﻿using Voxta.Abstractions.Diagnostics;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Repositories;
using Voxta.Abstractions.Services;
using Voxta.Shared.LargeLanguageModelsUtils;

namespace Voxta.Services.KoboldAI;

public class KoboldAISummarizationService : KoboldAIClientBase, ISummarizationService
{
    private readonly IPerformanceMetrics _performanceMetrics;
    private readonly IServiceObserver _serviceObserver;

    public KoboldAISummarizationService(IHttpClientFactory httpClientFactory, ISettingsRepository settingsRepository, IPerformanceMetrics performanceMetrics, IServiceObserver serviceObserver)
        :base(httpClientFactory, settingsRepository)
    {
        _performanceMetrics = performanceMetrics;
        _serviceObserver = serviceObserver;
    }

    public async ValueTask<string> SummarizeAsync(IChatInferenceData chat, CancellationToken cancellationToken)
    {
        var builder = new GenericPromptBuilder(Tokenizer);
        var prompt = builder.BuildSummarizationPrompt(chat);
        _serviceObserver.Record(ServiceObserverKeys.SummarizationService, KoboldAIConstants.ServiceName);
        _serviceObserver.Record(ServiceObserverKeys.SummarizationPrompt, prompt);
        
        var actionInferencePerf = _performanceMetrics.Start($"{KoboldAIConstants.ServiceName}.Summarization");
        var action = await SendCompletionRequest(BuildRequestBody(prompt, Array.Empty<string>()), cancellationToken);
        actionInferencePerf.Done();

        var result = action.TrimExcess();
        _serviceObserver.Record(ServiceObserverKeys.SummarizationResult, result);
        return result;
    }
}