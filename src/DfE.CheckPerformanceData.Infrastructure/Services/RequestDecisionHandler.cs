using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.Services;

public sealed class RequestDecisionHandler : IRequestDecisionHandler
{
    private readonly IZendeskService _zendeskService;
    private readonly IZendeskAttachmentService _zendeskAttachmentService;
    private readonly IZendeskTicketFieldService _ticketFieldService;
    private readonly BlobContainerClient _blobContainerClient;
    private readonly SchoolCheckingExerciseSettings _checkingExerciseSettings;
    private readonly ILogger<RequestDecisionHandler> _logger;

    public RequestDecisionHandler(
        IZendeskService zendeskService,
        IZendeskAttachmentService zendeskAttachmentService,
        IZendeskTicketFieldService ticketFieldService,
        BlobContainerClient blobContainerClient,
        IOptions<SchoolCheckingExerciseSettings> schoolCheckingExerciseSettings,
        ILogger<RequestDecisionHandler> logger)
    {
        _zendeskService = zendeskService;
        _zendeskAttachmentService = zendeskAttachmentService;
        _ticketFieldService = ticketFieldService;
        _blobContainerClient = blobContainerClient;
        if (schoolCheckingExerciseSettings?.Value == null)
            throw new ArgumentException("The School Checking Exercise Settings are required.");
        _checkingExerciseSettings = schoolCheckingExerciseSettings.Value;
        _logger = logger;
    }

    public async Task HandleAsync(RequestDocument message, CancellationToken token)
    {
        switch (message.DecisionType)
        {
            case DecisionType.AutoApproved:
                await HandleApprovedAsync(message as ApprovedRequestMessage, token);
                break;
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
            "Processing approved request: WindowId={WindowId}, DecisionType={DecisionType}",
            message.CheckingWindowId, message.DecisionType);

        var ticketRequest = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Description = $"Request for window {message.CheckingWindowId} has been approved.\nReason: {message.Reason}",
                Priority = "normal",
            }
        };

        ticketRequest = MapViewFields(message, ticketRequest);

        var response = await _zendeskService.CreateTicketAsync(ticketRequest);
        await UploadFilesAsync(message, response, token);

        _logger.LogInformation(
            "Created Zendesk ticket {TicketId} for approved request",
            response.Ticket.Id);
    }

    private async Task UploadFilesAsync(RequestDocument message, CreateTicketResponseDto response, CancellationToken token)
    {
        var files = message.Answers.SelectMany(x => x.Files ?? Enumerable.Empty<FileRecord>()).ToList();
        if (files.Any())
        {
            foreach (var upload in files)
            {
                await UploadAttachmentToTicketAsync(response.Ticket.Id, upload, token);
            }
        }
    }

    private async Task HandleRejectedAsync(RejectedRequestMessage? message, CancellationToken token)
    {
        if (message == null)
        {
            _logger.LogError("RejectedRequestMessage is null");
            return;
        }

        _logger.LogInformation(
            "Processing rejected request: WindowId={WindowId}, DecisionType={DecisionType}",
            message.CheckingWindowId, message.DecisionType);

        var ticketRequest = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Description = $"Request for window {message.CheckingWindowId} has been rejected.\nReason: {message.Reason}",
                Priority = "normal",
            }
        };

        ticketRequest = MapViewFields(message, ticketRequest);

        var response = await _zendeskService.CreateTicketAsync(ticketRequest);

        await UploadFilesAsync(message, response, token);

        _logger.LogInformation(
            "Created Zendesk ticket {TicketId} for rejected request",
            response.Ticket.Id);
    }

    private async Task HandleScrutinyAsync(ScrutinyMessage? message, CancellationToken token)
    {
        if (message == null)
        {
            _logger.LogError("ScrutinyMessage is null");
            return;
        }

        _logger.LogInformation(
            "Processing scrutiny request: WindowId={WindowId}",
            message.CheckingWindowId);

        var ticketRequest = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {                
                Description = $"Request for window {message.CheckingWindowId} requires scrutiny.\nReason: {message.Reason}",
                Priority = "high",
            }
        };

        ticketRequest = MapViewFields(message, ticketRequest);
        var response = await _zendeskService.CreateTicketAsync(ticketRequest);

        _logger.LogInformation(
            "Created Zendesk ticket {TicketId} for scrutiny request",
            response.Ticket.Id);
    }

    private async Task UploadAttachmentToTicketAsync(long ticketId, FileRecord upload, CancellationToken token)
    {
        try
        {
            var blobClient = _blobContainerClient.GetBlobClient(upload.StoredFileName.ToString());
            using var stream = await blobClient.OpenReadAsync(cancellationToken: token);
            await _zendeskAttachmentService.AddAttachmentAsync(
                ticketId, upload.OriginalFileName, stream, $"Evidence: {upload.OriginalFileName}");

            _logger.LogInformation(
                "Uploaded attachment '{OriginalFileName}' to ticket {TicketId}",
                upload.OriginalFileName, ticketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to upload attachment '{OriginalFileName}' (StoredFileName={StoredFileName}) to ticket {TicketId}",
                upload.OriginalFileName, upload.StoredFileName, ticketId);
        }
    }

    private CreateTicketRequestDto MapViewFields(RequestDocument message, CreateTicketRequestDto dto)
    {
        dto.Ticket.Subject = "School Checking Exercise";
        dto.Ticket.BrandId = _checkingExerciseSettings.BrandId;
        dto.Ticket.GroupId = _checkingExerciseSettings.GroupId;
        dto.Ticket.Status = "new";
        dto.Ticket.Type = "question";

        // Build description from message
        dto.Ticket.Description = string.IsNullOrWhiteSpace(dto.Ticket.Description)
            ? $"Request for window {message.CheckingWindowId} ({message.CheckingWindowType}). " +
              $"School: {message.School.Name} ({message.School.Urn}). " +
              $"Pupil: {message.Pupil.Firstname} {message.Pupil.Surname} (DOB: {message.Pupil.DateOfBirth}). " +
              $"Change requested: {message.WhatToChange}"
            : dto.Ticket.Description;

        // Map Decision Status using options constant
        var decisionStatusId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionStatusName);
        if (decisionStatusId.HasValue)
        {
            var decisionValue = message.DecisionType switch
            {
                DecisionType.Scrutiny => ZendeskTicketFieldOptions.DecisionStatus.Scrutiny,
                DecisionType.AutoApproved => ZendeskTicketFieldOptions.DecisionStatus.AutoApproved,
                DecisionType.AutoRejected => ZendeskTicketFieldOptions.DecisionStatus.AutoRejected,
                _ => null
            };

            if (decisionValue != null)
            {
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = decisionStatusId.Value,
                    Value = decisionValue
                });
            }
        }

        // Map School URN
        var schoolUrnId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.SchoolUrnName);
        if (schoolUrnId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = schoolUrnId.Value,
                Value = message.School.Urn
            });
        }

        // Map CYPMD ID
        var cypmdId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CypmdName);
        if (cypmdId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = cypmdId.Value,
                Value = message.Pupil.CypmdId
            });
        }

        // Map UPN
        var upnId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.UpnName);
        if (upnId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = upnId.Value,
                Value = message.Pupil.Id
            });
        }

        // Map LDS Matched Pupil ID
        var ldsMatchedPupilId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.LdsMatchedPupilIdName);
        if (ldsMatchedPupilId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = ldsMatchedPupilId.Value,
                Value = 0 // TODO: Map to actual LDS matched pupil ID when available in the payload
            });
        }

        // Map Surname
        var surnameId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.SurnameCypmdName);
        if (surnameId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = surnameId.Value,
                Value = message.Pupil.Surname.ToUpperInvariant()
            });
        }

        // Map Forename
        var forenameId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ForenameCypmdName);
        if (forenameId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = forenameId.Value,
                Value = message.Pupil.Firstname.ToUpperInvariant()
            });
        }

        // Map Date of Birth
        var dobId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DateOfBirthCypmdName);
        if (dobId.HasValue)
        {
            if (DateTime.TryParseExact(message.Pupil.DateOfBirth, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out var dob))
            {
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = dobId.Value,
                    Value = dob.ToString("yyyy-MM-dd")
                });
            }
            else
            {
                _logger.LogWarning("Unable to parse DateOfBirth '{DateOfBirth}' for pupil, skipping field.", message.Pupil.DateOfBirth);
            }
        }

        // Map Sex using options constant
        var sexId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.SexName);
        if (sexId.HasValue)
        {
            var sexValue = _ticketFieldService.GetOptionValue(
                ZendeskTicketFieldConstants.SexName, message.Pupil.Sex);
            if (sexValue != null)
            {
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = sexId.Value,
                    Value = sexValue
                });
            }
        }

        return dto;
    }
}