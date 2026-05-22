using System.Globalization;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.QueueMessages;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.Services;

/// <summary>
/// Turns a parsed <see cref="RequestMessage"/> + engine-produced
/// <see cref="Decision"/> into a Zendesk ticket. Dispatch is on
/// <see cref="Decision.Status"/>; the on-the-wire <c>DecisionType</c> is
/// informational only. Evidence uploads (if any) are attached for auto-decisions.
/// </summary>
public sealed class RequestDecisionHandler : IRequestDecisionHandler
{
    private const string EvidenceFolder = "Evidence";

    private readonly IZendeskService _zendeskService;
    private readonly IZendeskAttachmentService _zendeskAttachmentService;
    private readonly IZendeskTicketFieldService _ticketFieldService;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly SchoolCheckingExerciseSettings _checkingExerciseSettings;
    private readonly ILogger<RequestDecisionHandler> _logger;

    public RequestDecisionHandler(
        IZendeskService zendeskService,
        IZendeskAttachmentService zendeskAttachmentService,
        IZendeskTicketFieldService ticketFieldService,
        BlobServiceClient blobServiceClient,
        IOptions<SchoolCheckingExerciseSettings> schoolCheckingExerciseSettings,
        ILogger<RequestDecisionHandler> logger)
    {
        _zendeskService = zendeskService;
        _zendeskAttachmentService = zendeskAttachmentService;
        _ticketFieldService = ticketFieldService;
        _blobServiceClient = blobServiceClient;
        _checkingExerciseSettings = schoolCheckingExerciseSettings?.Value
            ?? throw new ArgumentException("The School Checking Exercise Settings are required.");
        _logger = logger;
    }

    public async Task HandleAsync(RequestMessage message, Decision decision, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(decision);

        _logger.LogInformation(
            "Processing decision: Status={Status} Outcome={Outcome} Rule={Rule} RequestId={RequestId}",
            decision.Status, decision.OutcomeKey, decision.MatchedRuleId, message.RequestId);

        var ticket = BuildTicket(message, decision);
        var response = await _zendeskService.CreateTicketAsync(ticket);

        // Attach evidence for auto-decisions whose message carries uploads.
        // Scrutiny tickets defer uploads to the human reviewer.
        if (decision.Status != DecisionStatus.Scrutiny &&
            message is IRequestMessageUploads withUploads &&
            withUploads.Uploads is { Count: > 0 } uploads)
        {
            foreach (var upload in uploads)
            {
                await UploadAttachmentToTicketAsync(response.Ticket.Id, message.CheckingWindowId, upload, token);
            }
        }

        _logger.LogInformation(
            "Created Zendesk ticket {TicketId} for RequestId={RequestId} (Decision={Status}, Rule={Rule}).",
            response.Ticket.Id, message.RequestId, decision.Status, decision.MatchedRuleId);
    }

    private CreateTicketRequestDto BuildTicket(RequestMessage message, Decision decision)
    {
        var subject = decision.Status switch
        {
            DecisionStatus.AutoApproved => $"CPMD Auto-Approved: {decision.OutcomeKey} ({message.RequestId})",
            DecisionStatus.AutoRejected => $"CPMD Auto-Rejected: {decision.OutcomeKey} ({message.RequestId})",
            DecisionStatus.Scrutiny     => $"CPMD Requires Scrutiny: {decision.OutcomeKey} ({message.RequestId})",
            _                           => $"CPMD: {decision.OutcomeKey} ({message.RequestId})",
        };

        var priority = decision.Status == DecisionStatus.Scrutiny ? "high" : "normal";
        var status = decision.Status == DecisionStatus.Scrutiny ? "new" : "open";
        var type = decision.Status == DecisionStatus.Scrutiny ? "question" : "task";

        var dto = new CreateTicketRequestDto
        {
            Ticket = new CreateTicketDto
            {
                Subject = subject,
                Description = BuildDescription(message, decision),
                Status = status,
                Priority = priority,
                Type = type,
                BrandId = _checkingExerciseSettings.BrandId,
                GroupId = _checkingExerciseSettings.GroupId,
            }
        };

        AddEngineCustomFields(dto, decision);
        MapPupilFields(dto, message);
        return dto;
    }

    private static string BuildDescription(RequestMessage message, Decision decision)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Request {message.RequestId} for window {message.CheckingWindowId} ({message.CheckingWindowType}).");
        sb.AppendLine($"Outcome: {decision.OutcomeKey}");
        sb.AppendLine($"Decision: {decision.Status}");
        sb.AppendLine($"Matched rule: {decision.MatchedRuleId}");
        sb.AppendLine();
        if (decision.Trace.Count > 0)
        {
            sb.AppendLine("Trace:");
            foreach (var line in decision.Trace)
            {
                sb.AppendLine($"  - {line}");
            }
            sb.AppendLine();
        }

        var reason = ExtractReason(message);
        if (!string.IsNullOrWhiteSpace(reason))
        {
            sb.AppendLine($"Reason supplied by school: {reason}");
        }

        return sb.ToString();
    }

    private static string? ExtractReason(RequestMessage message) => message switch
    {
        PendingRequestMessage p  => p.Reason,
        ApprovedRequestMessage a => a.Reason,
        RejectedRequestMessage r => r.Reason,
        ScrutinyMessage s        => s.Reason,
        _ => null,
    };

    private void AddEngineCustomFields(CreateTicketRequestDto dto, Decision decision)
    {
        dto.Ticket.CustomFields ??= new List<CustomFieldDto>();

        if (_checkingExerciseSettings.DecisionStatusCustomFieldId > 0)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = _checkingExerciseSettings.DecisionStatusCustomFieldId,
                Value = decision.Status.ToString(),
            });
        }

        if (_checkingExerciseSettings.OutcomeKeyCustomFieldId > 0)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = _checkingExerciseSettings.OutcomeKeyCustomFieldId,
                Value = decision.OutcomeKey,
            });
        }

        if (_checkingExerciseSettings.MatchedRuleIdCustomFieldId > 0)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = _checkingExerciseSettings.MatchedRuleIdCustomFieldId,
                Value = decision.MatchedRuleId,
            });
        }
    }

    private void MapPupilFields(CreateTicketRequestDto dto, RequestMessage message)
    {
        dto.Ticket.CustomFields ??= new List<CustomFieldDto>();

        var schoolUrnId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.SchoolUrnName);
        if (schoolUrnId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = schoolUrnId.Value,
                Value = message.School.Urn,
            });
        }

        var cypmdFieldId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CypmdName);
        if (cypmdFieldId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = cypmdFieldId.Value,
                Value = message.Pupil.CypmdId,
            });
        }

        var upnId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.UpnName);
        if (upnId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = upnId.Value,
                Value = message.Pupil.Id,
            });
        }

        var surnameId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.SurnameCypmdName);
        if (surnameId.HasValue && !string.IsNullOrEmpty(message.Pupil.Surname))
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = surnameId.Value,
                Value = message.Pupil.Surname.ToUpperInvariant(),
            });
        }

        var forenameId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ForenameCypmdName);
        if (forenameId.HasValue && !string.IsNullOrEmpty(message.Pupil.Firstname))
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = forenameId.Value,
                Value = message.Pupil.Firstname.ToUpperInvariant(),
            });
        }

        var dobId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DateOfBirthCypmdName);
        if (dobId.HasValue && !string.IsNullOrEmpty(message.Pupil.DateOfBirth))
        {
            if (DateTime.TryParseExact(message.Pupil.DateOfBirth, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
            {
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = dobId.Value,
                    Value = dob.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                });
            }
            else
            {
                _logger.LogWarning("Unable to parse DateOfBirth '{DateOfBirth}' for pupil, skipping field.", message.Pupil.DateOfBirth);
            }
        }

        var sexId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.SexName);
        if (sexId.HasValue && !string.IsNullOrEmpty(message.Pupil.Sex))
        {
            var sexValue = _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.SexName, message.Pupil.Sex);
            if (sexValue != null)
            {
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = sexId.Value,
                    Value = sexValue,
                });
            }
        }
    }

    private async Task UploadAttachmentToTicketAsync(long ticketId, Guid checkingWindowId, UploadInfo upload, CancellationToken token)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(checkingWindowId.ToString());
            var blobClient = container.GetBlobClient($"{EvidenceFolder}/{upload.Id}");
            using var stream = await blobClient.OpenReadAsync(cancellationToken: token);
            await _zendeskAttachmentService.AddAttachmentAsync(
                ticketId, upload.Filename, stream, $"Evidence: {upload.Filename}");

            _logger.LogInformation(
                "Uploaded attachment '{Filename}' (Id={Id}) to ticket {TicketId}.",
                upload.Filename, upload.Id, ticketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to upload attachment '{Filename}' (Id={Id}) to ticket {TicketId}.",
                upload.Filename, upload.Id, ticketId);
        }
    }
}
