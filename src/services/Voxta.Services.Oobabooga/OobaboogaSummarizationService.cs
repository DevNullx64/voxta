﻿using Voxta.Abstractions.Diagnostics;
using Voxta.Abstractions.Model;
using Voxta.Abstractions.Repositories;
using Voxta.Abstractions.Services;
using Voxta.Shared.LargeLanguageModelsUtils;

namespace Voxta.Services.Oobabooga;

public class OobaboogaSummarizationService : OobaboogaClientBase, ISummarizationService
{
    private readonly IPerformanceMetrics _performanceMetrics;
    private readonly IServiceObserver _serviceObserver;

    public OobaboogaSummarizationService(IHttpClientFactory httpClientFactory, ISettingsRepository settingsRepository, IPerformanceMetrics performanceMetrics, IServiceObserver serviceObserver)
        :base(httpClientFactory, settingsRepository)
    {
        _performanceMetrics = performanceMetrics;
        _serviceObserver = serviceObserver;
    }

    public async ValueTask<string> SummarizeAsync(IChatInferenceData chat, CancellationToken cancellationToken)
    {
        var builder = new GenericPromptBuilder(Tokenizer);
        var prompt = builder.BuildSummarizationPrompt(chat);
        _serviceObserver.Record(ServiceObserverKeys.SummarizationService, OobaboogaConstants.ServiceName);
        _serviceObserver.Record(ServiceObserverKeys.SummarizationPrompt, prompt);
        
        var actionInferencePerf = _performanceMetrics.Start($"{OobaboogaConstants.ServiceName}.Summarization");
        var body = BuildRequestBody(prompt, builder.SummarizationStopTokens);
        body.Temperature = 0.1;
        var action = await SendCompletionRequest(body, cancellationToken);
        actionInferencePerf.Done();

        var result = action.TrimExcess();
        _serviceObserver.Record(ServiceObserverKeys.SummarizationResult, result);
        return result;
    }
}