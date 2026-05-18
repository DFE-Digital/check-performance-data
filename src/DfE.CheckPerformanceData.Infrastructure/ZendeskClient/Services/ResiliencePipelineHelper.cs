using DfE.CheckPerformanceData.Application.ZendeskClient;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Refit;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;

public static class ResiliencePipelineHelper
{
    /// <summary>
    /// Executes an async action with retry resilience. Logs success, failures, and errors.
    /// </summary>
    public static async Task<T> ExecuteWithResilienceAsync<T>(
        ResiliencePipeline pipeline,
        Func<Task<T>> action,
        string operationName,
        ILogger? logger = null,
        Action? onSuccess = null,
        Func<Exception, ZendeskApiException>? errorFactory = null)
    {
        if (logger != null)
            logger.LogDebug("Executing Zendesk operation: {OperationName}", operationName);

        try
        {
            var result = await pipeline.ExecuteAsync(
                async ct => await action(),
                CancellationToken.None);

            onSuccess?.Invoke();
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (!IsZendeskApiException(ex))
        {
            var message = $"Error during Zendesk operation: {operationName}";

            if (logger != null)
                logger.LogError(ex, "{Message}", message);

            if (errorFactory != null)
                throw errorFactory(ex);

            var inner = ex.InnerException ?? ex;
            throw new ZendeskApiException($"An unexpected error occurred during {operationName}.", inner);
        }
    }

    /// <summary>
    /// Executes an async action with retry resilience, mapping the result through a function.
    /// </summary>
    public static async Task<TDto> ExecuteWithResilienceAndMapAsync<TIn, TDto>(
        ResiliencePipeline pipeline,
        Func<Task<TIn>> action,
        Func<TIn, TDto> mapper,
        string operationName,
        ILogger? logger = null,
        Action? onSuccess = null,
        Func<Exception, ZendeskApiException>? errorFactory = null)
    {
        try
        {
            var result = await pipeline.ExecuteAsync(
                async ct => await action(),
                CancellationToken.None);

            onSuccess?.Invoke();
            return mapper(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (!IsZendeskApiException(ex))
        {
            var message = $"Error during Zendesk operation: {operationName}";

            if (logger != null)
                logger.LogError(ex, "{Message}", message);

            if (errorFactory != null)
                throw errorFactory(ex);

            var inner = ex.InnerException ?? ex;
            throw new ZendeskApiException($"An unexpected error occurred during {operationName}.", inner);
        }
    }

    /// <summary>
    /// Returns true if the exception is a <see cref="ZendeskApiException"/> or wraps one.
    /// </summary>
    public static bool IsZendeskApiException(Exception ex)
    {
        return ex is ZendeskApiException || (ex.InnerException != null && IsZendeskApiException(ex.InnerException));
    }

    public static ResiliencePipeline CreateRetryPipeline(PollySettings settings, ILogger logger)
    {
        var builder = new ResiliencePipelineBuilder();

        // Exclude client error status codes from retry since they represent non-transient failures:
        // - 400 Bad Request: malformed request, retrying won't fix it
        // - 401 Unauthorized: authentication failure, retrying won't fix it
        // - 403 Forbidden: permission denied, retrying won't fix it
        // - 404 Not Found: resource doesn't exist, retrying won't fix it
        // - 422 Unprocessable Entity: semantic/business validation error, retrying won't fix it
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
                    // For other exceptions, check if they wrap an ApiException as an inner exception
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
}