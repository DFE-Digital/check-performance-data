using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Microsoft.Extensions.Logging;
using Notify.Exceptions;
using Polly;
using Polly.Retry;
using Refit;

namespace DfE.CheckPerformanceData.Infrastructure.Resilience;

public static class ResiliencePipelineFactory
{
    public static ResiliencePipeline CreateRetryPipeline(PollySettings settings, ILogger logger)
    {
        var builder = new ResiliencePipelineBuilder();

        int[] nonRetryStatusCodes = { 400, 401, 403, 404, 422 };

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
                .Handle<ApiException>(ex => !nonRetryStatusCodes.Contains((int)ex.StatusCode))
                .Handle<Exception>(ex =>
                {
                    if (ex.InnerException is ApiException apiEx)
                    {
                        return !nonRetryStatusCodes.Contains((int)apiEx.StatusCode);
                    }
                    return false;
                }),
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

    public static ResiliencePipeline CreateNotifyRetryPipeline(PollySettings settings, ILogger logger)
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
                .Handle<NotifyClientException>(),
            OnRetry = args =>
            {
                logger.LogWarning(
                    args.Outcome.Exception,
                    "Notify API request failed. Retry attempt {RetryAttempt}",
                    args.AttemptNumber);

                return ValueTask.CompletedTask;
            }
        });

        return builder.Build();
    }
}
