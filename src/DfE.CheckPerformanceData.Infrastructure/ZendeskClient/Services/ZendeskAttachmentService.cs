using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Mappers;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services
{
    public class ZendeskAttachmentService : IZendeskAttachmentService
    {
        private readonly IZendeskApi _api;
        private readonly PollySettings _settings;
        private readonly ResiliencePipeline _resiliencePipeline;
        private readonly ILogger<ZendeskAttachmentService> _logger;
        

        public ZendeskAttachmentService(IZendeskApi api, IOptions<PollySettings> settings, ILogger<ZendeskAttachmentService> logger)
        {
            _api = api;
            _settings = settings.Value;
            _logger = logger;
        

            var builder = new ResiliencePipelineBuilder();

            builder.AddRetry(new RetryStrategyOptions
            {
                DelayGenerator = (args) =>
                {
                    var delay = Math.Pow(2, args.AttemptNumber - 1) * _settings.BaseDelayMilliseconds;
                    var jitter = new Random().Next(0, _settings.JitterMilliseconds);
                    return ValueTask.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(delay + jitter));
                },
                MaxRetryAttempts = _settings.MaxRetryAttempts,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<Exception>(ex => ex.GetType().Namespace?.StartsWith("Refit") == true
                        || ex.GetType().Namespace?.StartsWith("System.Net") == true),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Zendesk API request failed. Retry attempt {RetryAttempt}",
                        args.AttemptNumber);

                    return ValueTask.CompletedTask;
                }
            });

            _resiliencePipeline = builder.Build();
        }

        /// <summary>
        /// Uploads a file to Zendesk and attaches it to an existing ticket as a comment.
        /// </summary>
        public async Task<UpdateTicketResponseDto> AddAttachmentAsync(
            long ticketId,
            string fileName,
            Stream fileStream,
            string commentBody = "Attachment uploaded"
        )
        {
            try
            {
                if (ticketId == 0)
                    throw new ArgumentException("Ticket Id is not valid.", nameof(ticketId));
                if (fileStream == null || fileStream.Length == 0)
                    throw new ArgumentException("File stream is empty", nameof(fileStream));

                // STEP 1 — Upload file to Zendesk
                var uploadResponse = await ExecuteWithResilienceAsync(
                    () => _api.UploadFile(fileName, fileStream),
                    $"UploadFile({fileName}) for ticket {ticketId}");

                var token = uploadResponse?.Upload?.Token;
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidOperationException("Zendesk did not return an upload token.");

                // STEP 2 — Add comment referencing the upload token
                var request = new UpdateTicketRequest
                {
                    Ticket = new UpdateTicket
                    {
                        Comment = new TicketCommentUpdate
                        {
                            Body = commentBody,
                            Uploads = new List<string> { token }
                        }
                    }
                };

                var response = await ExecuteWithResilienceAsync(
                    () => _api.AddCommentWithAttachment(ticketId, request),
                    $"AddCommentWithAttachment({ticketId})");

                return ZendeskMapper.ToDto(response);
            }
            catch (ZendeskApiException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading attachment to ticket {TicketId}", ticketId);
                throw new ZendeskApiException(
                    $"Failed to upload attachment to ticket {ticketId}.",
                    ex);
            }
        }

        private async Task<T> ExecuteWithResilienceAsync<T>(Func<Task<T>> action, string operationName)
        {
            try
            {
                _logger.LogDebug("Executing Zendesk operation: {OperationName}", operationName);
                var result = await _resiliencePipeline.ExecuteAsync(
                    async ct => await action(),
                    CancellationToken.None);
                _logger.LogDebug("Successfully completed Zendesk operation: {OperationName}", operationName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resilience pipeline failed for operation: {OperationName}", operationName);
                throw;
            }
        }
    }

}
