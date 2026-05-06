using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;

public static class ResiliencePipelineHelper
{
    public static ResiliencePipeline CreateRetryPipeline(PollySettings settings, ILogger logger)
    {
        var builder = new ResiliencePipelineBuilder();

        builder.AddRetry(new RetryStrategyOptions
        {
            DelayGenerator = (args) =>
            {
                var delay = Math.Pow(2, args.AttemptNumber - 1) * settings.BaseDelayMilliseconds;
                var jitter = Random.Shared.Next(0, settings.JitterMilliseconds);
                return ValueTask.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(delay + jitter));
            },
            MaxRetryAttempts = settings.MaxRetryAttempts,
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<Exception>(ex => ex.GetType().Namespace?.StartsWith("Refit") == true
                    || ex.GetType().Namespace?.StartsWith("System.Net") == true),
            OnRetry = args =>
            {
                logger.LogWarning(
                    args.Outcome.Exception,
                    "Zendesk API request failed. Retry attempt {RetryAttempt}",
                    args.AttemptNumber);

                return ValueTask.CompletedTask;
            }
        });

        return builder.Build();
    }
}
