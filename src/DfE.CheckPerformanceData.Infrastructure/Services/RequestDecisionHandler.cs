using System.Globalization;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.RequestDecision;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Infrastructure.Services;

/// <summary>
/// Turns a parsed <see cref="RequestDocument"/> + engine-produced
/// <see cref="Decision"/> into a Zendesk ticket. Dispatch is on
/// <see cref="Decision.Status"/> — the rules engine is the sole decision-maker.
/// Any evidence the school uploaded is attached, regardless of decision.
/// </summary>
public sealed class RequestDecisionHandler : IRequestDecisionHandler
{
    // Matches the folder EvidenceBlobStorageService writes uploads to.
    private const string EvidenceFolder = "evidence-uploads";

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

    public async Task HandleAsync(RequestDocument message, Decision decision, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(decision);

        _logger.LogInformation(
            "Processing decision: Status={Status} Outcome={Outcome} Rule={Rule} Reference={Reference}",
            decision.Status, decision.OutcomeKey, decision.MatchedRuleId, message.ReferenceNumber);

        var ticket = BuildTicket(message, decision);
        var response = await _zendeskService.CreateTicketAsync(ticket);

        // Always attach the evidence the school uploaded, regardless of decision.
        // Scrutiny especially needs the files in front of the caseworker.
        var files = message.Answers
            .Where(a => a.Files is not null)
            .SelectMany(a => a.Files!);
        foreach (var file in files)
        {
            await UploadAttachmentToTicketAsync(response.Ticket.Id, message.CheckingWindowId, file, token);
        }

        _logger.LogInformation(
            "Created Zendesk ticket {TicketId} for Reference={Reference} (Decision={Status}, Rule={Rule}).",
            response.Ticket.Id, message.ReferenceNumber, decision.Status, decision.MatchedRuleId);
    }

    private CreateTicketRequestDto BuildTicket(RequestDocument message, Decision decision)
    {
        var subject = decision.Status switch
        {
            DecisionStatus.AutoApproved => $"CPMD Auto-Approved: {decision.OutcomeKey} ({message.ReferenceNumber})",
            DecisionStatus.AutoRejected => $"CPMD Auto-Rejected: {decision.OutcomeKey} ({message.ReferenceNumber})",
            DecisionStatus.Scrutiny     => $"CPMD Requires Scrutiny: {decision.OutcomeKey} ({message.ReferenceNumber})",
            _                           => $"CPMD: {decision.OutcomeKey} ({message.ReferenceNumber})",
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

    private static string BuildDescription(RequestDocument message, Decision decision)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Request {message.ReferenceNumber} for window {message.CheckingWindowId} ({message.CheckingWindowType}).");
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

        // Surface the school's answers so the caseworker sees the context behind
        // the decision — most valuable on Scrutiny tickets.
        var answers = message.Answers.Where(a => !string.IsNullOrWhiteSpace(a.Value)).ToList();
        if (answers.Count > 0)
        {
            sb.AppendLine("Answers:");
            foreach (var answer in answers)
            {
                sb.AppendLine($"  - {answer.QuestionTitle}: {answer.Value}");
            }
        }

        return sb.ToString();
    }

    private void AddEngineCustomFields(CreateTicketRequestDto dto, Decision decision)
    {
        dto.Ticket.CustomFields ??= new List<CustomFieldDto>();
        var decisionStatusFieldId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionStatusName);
        if (decisionStatusFieldId.HasValue && decisionStatusFieldId.Value > 0)
        //if (_checkingExerciseSettings.DecisionStatusCustomFieldId > 0)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = decisionStatusFieldId.Value, // todo investigaet and remove these settings ://_checkingExerciseSettings.DecisionStatusCustomFieldId,
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

    private void MapPupilFields(CreateTicketRequestDto dto, RequestDocument message)
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

    private async Task UploadAttachmentToTicketAsync(long ticketId, Guid checkingWindowId, FileRecord file, CancellationToken token)
    {
        try
        {
            var container = _blobServiceClient.GetBlobContainerClient(checkingWindowId.ToString());
            var blobClient = container.GetBlobClient($"{EvidenceFolder}/{file.StoredFileName}");
            using var stream = await blobClient.OpenReadAsync(cancellationToken: token);
            await _zendeskAttachmentService.AddAttachmentAsync(
                ticketId, file.OriginalFileName, stream, $"Evidence: {file.OriginalFileName}");

            _logger.LogInformation(
                "Uploaded attachment '{Filename}' (Stored={Stored}) to ticket {TicketId}.",
                file.OriginalFileName, file.StoredFileName, ticketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to upload attachment '{Filename}' (Stored={Stored}) to ticket {TicketId}.",
                file.OriginalFileName, file.StoredFileName, ticketId);
        }
    }
}
