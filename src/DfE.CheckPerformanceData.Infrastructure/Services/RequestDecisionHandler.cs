using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Domain.QueueMessages;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Infrastructure.Services;

public class RequestDecisionHandler : IRequestDecisionHandler
{
    private readonly IZendeskService _zendeskService;
    private readonly IZendeskAttachmentService _zendeskAttachmentService;
    private readonly BlobContainerClient _blobContainerClient;
    private readonly SchoolCheckingExerciseSettings _checkingExerciseSettings;
    private readonly ILogger<RequestDecisionHandler> _logger;

    public RequestDecisionHandler(
        IZendeskService zendeskService,
        IZendeskAttachmentService zendeskAttachmentService,
        BlobContainerClient blobContainerClient,
        IOptions<SchoolCheckingExerciseSettings> schoolCheckingExerciseSettings,
        ILogger<RequestDecisionHandler> logger)
    {
        _zendeskService = zendeskService;
        _zendeskAttachmentService = zendeskAttachmentService;
        _blobContainerClient = blobContainerClient;
        if (schoolCheckingExerciseSettings?.Value == null)
            throw new ArgumentException("The School Checking Exercise Settings are required.");
        _checkingExerciseSettings = schoolCheckingExerciseSettings.Value;
        _logger = logger;
    }

    public async Task HandleAsync(RequestMessage message, CancellationToken token)
    {
        switch (message.DecisionType)
        {
           // case DecisionType.Approved:
            case DecisionType.AutoApproved:
                await HandleApprovedAsync(message as ApprovedRequestMessage, token);
                break;
           // case DecisionType.Rejected:
            case DecisionType.AutoRejected:
                await HandleRejectedAsync(message as RejectedRequestMessage, token);
                break;
            case DecisionType.Scrutiny:
                await HandleScrutinyAsync(message as ScrutinyMessage, token);
                break;
            default:
                _logger.LogWarning("Unhandled decision type: {DecisionType}", message.DecisionType);
                break;
        }
    }

    private async Task HandleApprovedAsync(ApprovedRequestMessage? message, CancellationToken token)
    {
        if (message == null)
        {
            _logger.LogError("ApprovedRequestMessage is null");
            return;
        }

        _logger.LogInformation(
            "Processing approved request: WindowId={WindowId}, RequestId={RequestId}, DecisionType={DecisionType}",
            message.CheckingWindowId, message.RequestId, message.DecisionType);

        var ticketRequest = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = $"CPMD Request Approved: {message.RequestId}",
                Description = $"Request {message.RequestId} for window {message.CheckingWindowId} has been approved.\nReason: {message.Reason}",
                Status = "open",
                Priority = "normal",
                Type = "task"
            }
        };

        var response = await _zendeskService.CreateTicketAsync(ticketRequest);

        if (message.Uploads.Any())
        {
            foreach (var upload in message.Uploads)
            {
                await UploadAttachmentToTicketAsync(response.Ticket.Id, upload, token);
            }
        }

        _logger.LogInformation(
            "Created Zendesk ticket {TicketId} for approved request {RequestId}",
            response.Ticket.Id, message.RequestId);
    }

    private async Task HandleRejectedAsync(RejectedRequestMessage? message, CancellationToken token)
    {
        if (message == null)
        {
            _logger.LogError("RejectedRequestMessage is null");
            return;
        }

        _logger.LogInformation(
            "Processing rejected request: WindowId={WindowId}, RequestId={RequestId}, DecisionType={DecisionType}",
            message.CheckingWindowId, message.RequestId, message.DecisionType);

        var ticketRequest = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = $"CPMD Request Rejected: {message.RequestId}",
                Description = $"Request {message.RequestId} for window {message.CheckingWindowId} has been rejected.\nReason: {message.Reason}",
                Status = "open",
                Priority = "normal",
                Type = "task"
            }
        };

        var response = await _zendeskService.CreateTicketAsync(ticketRequest);

        if (message.Uploads.Any())
        {
            foreach (var upload in message.Uploads)
            {
                await UploadAttachmentToTicketAsync(response.Ticket.Id, upload, token);
            }
        }

        _logger.LogInformation(
            "Created Zendesk ticket {TicketId} for rejected request {RequestId}",
            response.Ticket.Id, message.RequestId);
    }

    private async Task HandleScrutinyAsync(ScrutinyMessage? message, CancellationToken token)
    {
        if (message == null)
        {
            _logger.LogError("ScrutinyMessage is null");
            return;
        }

        _logger.LogInformation(
            "Processing scrutiny request: WindowId={WindowId}, RequestId={RequestId}",
            message.CheckingWindowId, message.RequestId);

        var ticketRequest = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = $"CPMD Request Requires Scrutiny: {message.RequestId}",
                Description = $"Request {message.RequestId} for window {message.CheckingWindowId} requires scrutiny.\nReason: {message.Reason}",
                Status = "open",
                Priority = "high",
                Type = "task",
                // add fields required to show in the view 
            }
        };

        ticketRequest = mapViewFields(message, ticketRequest);
        var response = await _zendeskService.CreateTicketAsync(ticketRequest);

        _logger.LogInformation(
            "Created Zendesk ticket {TicketId} for scrutiny request {RequestId}",
            response.Ticket.Id, message.RequestId);
    }

    private async Task UploadAttachmentToTicketAsync(long ticketId, UploadInfo upload, CancellationToken token)
    {
        try
        {
            var blobClient = _blobContainerClient.GetBlobClient(upload.Id.ToString());
            using var stream = await blobClient.OpenReadAsync(cancellationToken: token);
            await _zendeskAttachmentService.AddAttachmentAsync(
                ticketId, upload.Filename, stream, $"Evidence: {upload.Filename}");
            
            _logger.LogInformation(
                "Uploaded attachment '{Filename}' to ticket {TicketId}",
                upload.Filename, ticketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to upload attachment '{Filename}' (Id={Id}) to ticket {TicketId}",
                upload.Filename, upload.Id, ticketId);
        }
    }

    private CreateTicketRequestDto mapViewFields(RequestMessage message, CreateTicketRequestDto dto)
    {
        
        dto.Ticket.Subject = "School Checking Exercise";
        dto.Ticket.BrandId = _checkingExerciseSettings.BrandId;
        dto.Ticket.GroupId = _checkingExerciseSettings.GroupId;
        dto.Ticket.Status = "new";
        dto.Ticket.Type = "question";

        // todo - build description from message.

        // todo - decide how to map custom fields
        dto.Ticket.CustomFields.Add(new CustomFieldDto
        {
            Id = 19056253670034,
            Value = message.DecisionType.ToString()
        });


        return dto;

    }
}