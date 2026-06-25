using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Mappers;
using DfE.CheckPerformanceData.Infrastructure.Resilience;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;

public sealed class ZendeskAttachmentService : IZendeskAttachmentService
{
    private readonly IZendeskApi _api;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ILogger<ZendeskAttachmentService> _logger;

    public ZendeskAttachmentService(IZendeskApi api, IOptions<PollySettings> settings, ILogger<ZendeskAttachmentService> logger)
    {
        _api = api;
        _logger = logger;

        _resiliencePipeline = ResiliencePipelineFactory.CreateRetryPipeline(settings.Value, _logger);
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
                $"UploadFile({fileName}) for ticket {ticketId}",
                ex => new ZendeskApiException($"Failed to upload attachment to ticket {ticketId}.", ex));

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
                $"AddCommentWithAttachment({ticketId})",
                ex => new ZendeskApiException($"Failed to upload attachment to ticket {ticketId}.", ex));

            return ZendeskMapper.ToDto(response);
        }
        catch (ZendeskApiException)
        {
            throw;
        }
        catch (Exception ex) when (!ResiliencePipelineHelper.IsZendeskApiException(ex))
        {
            _logger.LogError(ex, "Error uploading attachment to ticket {TicketId}", ticketId);
            var inner = ex.InnerException ?? ex;
            throw new ZendeskApiException($"Failed to upload attachment to ticket {ticketId}.", inner);
        }
    }

    private async Task<T> ExecuteWithResilienceAsync<T>(Func<Task<T>> action, string operationName, Func<Exception, ZendeskApiException>? errorFactory = null)
    {
        return await ResiliencePipelineHelper.ExecuteWithResilienceAsync(
            _resiliencePipeline,
            () => action(),
            operationName,
            logger: _logger,
            onSuccess: () => _logger.LogDebug("Successfully completed Zendesk operation: {OperationName}", operationName),
            errorFactory: errorFactory ?? (ex => new ZendeskApiException($"An unexpected error occurred during {operationName}.", ex.InnerException ?? ex)));
    }
}
