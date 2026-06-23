using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
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
}