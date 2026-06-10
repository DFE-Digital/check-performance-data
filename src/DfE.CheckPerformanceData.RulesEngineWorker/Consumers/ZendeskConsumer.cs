using System.Globalization;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Consumers;

/// <summary>
/// Consumes ticket messages and turns each into a Zendesk ticket, attaching any
/// evidence the school uploaded. Creation is idempotent: the matching
/// <see cref="ChangeRequest"/>'s CRM id is checked before creating, so a redelivery
/// or redrive of the same message never opens a duplicate ticket.
/// </summary>
public sealed class ZendeskConsumer : ConsumerBase
{
    // Matches the folder EvidenceBlobStorageService writes uploads to.
    private const string EvidenceFolder = "evidence-uploads";

    // Scoped collaborators. In the hosting path they are rebound from the
    // per-message scope before each message is processed (the consumer loop is
    // strictly sequential, so there is no concurrent access); in the unit-test
    // path they are the directly-injected instances.
    private IZendeskService _zendeskService;
    private IPortalDbContext _dbContext;
    private IZendeskAttachmentService? _zendeskAttachmentService;

    private readonly IZendeskTicketFieldService? _ticketFieldService;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly SchoolCheckingExerciseSettings? _checkingExerciseSettings;
    private readonly ILogger _logger;

    // Process-local record of references already ticketed this lifetime. The
    // durable guard is ChangeRequest.CrmId (which survives a restart); this is a
    // cheap first line that stops a same-process redelivery before any Zendesk call.
    private readonly HashSet<string> _ticketed = new(StringComparer.Ordinal);
    private readonly object _ticketedLock = new();

    // Test constructor: collaborators are injected directly. Internal so the DI
    // container only ever sees the public hosting constructor below.
    internal ZendeskConsumer(
        IQueueService queueService,
        IZendeskService zendeskService,
        IPortalDbContext dbContext)
        : base(queueService, Options.Create(new QueueOptions()), NullLogger<ZendeskConsumer>.Instance)
    {
        _zendeskService = zendeskService;
        _dbContext = dbContext;
        _logger = NullLogger<ZendeskConsumer>.Instance;
    }

    // Hosting constructor: scoped collaborators (Zendesk service, attachment
    // service, DbContext, queue) are resolved per message from a fresh scope; the
    // ticket-field service, blob client and settings are singletons.
    public ZendeskConsumer(
        IServiceScopeFactory scopeFactory,
        IZendeskTicketFieldService ticketFieldService,
        BlobServiceClient blobServiceClient,
        IOptions<SchoolCheckingExerciseSettings> checkingExerciseSettings,
        IOptions<QueueOptions> options,
        ILogger<ZendeskConsumer> logger)
        : base(scopeFactory, options, logger)
    {
        _zendeskService = null!;
        _dbContext = null!;
        _ticketFieldService = ticketFieldService;
        _blobServiceClient = blobServiceClient;
        _checkingExerciseSettings = checkingExerciseSettings?.Value;
        _logger = logger;
    }

    protected override string QueueName => QueueOptions.ZendeskQueue;

    protected override Task ProcessMessageBodyAsync(
        string messageBody, IServiceProvider? services, CancellationToken cancellationToken)
    {
        if (services is not null)
        {
            _zendeskService = services.GetRequiredService<IZendeskService>();
            _dbContext = services.GetRequiredService<IPortalDbContext>();
            _zendeskAttachmentService = services.GetRequiredService<IZendeskAttachmentService>();
        }

        return ProcessMessageBodyAsync(messageBody, cancellationToken);
    }

    public override async Task ProcessMessageBodyAsync(string messageBody, CancellationToken cancellationToken)
    {
        var message = RequestDocumentParser.Parse(messageBody)
            ?? throw new InvalidOperationException("Failed to parse message.");

        lock (_ticketedLock)
        {
            if (_ticketed.Contains(message.ReferenceNumber))
            {
                return;
            }
        }

        var changeRequest = await LoadChangeRequestAsync(message.ReferenceNumber, cancellationToken);

        // Check-before-create: a redelivery of a request whose ticket already exists
        // (CrmId persisted from a prior delivery) is a no-op, so at-least-once
        // delivery never double-creates a ticket — even across a worker restart.
        if (!string.IsNullOrEmpty(changeRequest?.CrmId))
        {
            lock (_ticketedLock)
            {
                _ticketed.Add(message.ReferenceNumber);
            }
            return;
        }

        var decision = DeriveDecision(message, changeRequest);
        var ticket = BuildTicket(message, decision);
        var response = await _zendeskService.CreateTicketAsync(ticket);

        var files = message.Answers
            .Where(a => a.Files is not null)
            .SelectMany(a => a.Files!);
        foreach (var file in files)
        {
            await UploadAttachmentToTicketAsync(response.Ticket.Id, message.CheckingWindowId, file, cancellationToken);
        }

        var crmId = response.Ticket.Id.ToString(CultureInfo.InvariantCulture);

        lock (_ticketedLock)
        {
            _ticketed.Add(message.ReferenceNumber);
        }

        await _dbContext.ExecuteInTransactionAsync(async () =>
        {
            await _dbContext.ChangeRequests
                .Where(r => r.ReferenceNumber == message.ReferenceNumber)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.CrmId, crmId)
                    .SetProperty(r => r.Status, RequestStatus.ZendeskTicketCreated),
                    cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "Created Zendesk ticket {TicketId} for Reference={Reference} (Decision={Status}, Rule={Rule}).",
            response.Ticket.Id, message.ReferenceNumber, decision.Status, decision.MatchedRuleId);
    }

    private async Task<ChangeRequest?> LoadChangeRequestAsync(string referenceNumber, CancellationToken cancellationToken)
    {
        try
        {
            return await _dbContext.ChangeRequests
                .FirstOrDefaultAsync(r => r.ReferenceNumber == referenceNumber, cancellationToken);
        }
        catch (Exception ex)
        {
            // A failed read must not become a duplicate ticket: the process-local
            // guard and the post-create CrmId write still hold idempotency.
            _logger.LogWarning(ex,
                "Could not load change request for Reference={Reference}; proceeding to create.", referenceNumber);
            return null;
        }
    }

    private static Decision DeriveDecision(RequestDocument message, ChangeRequest? changeRequest)
    {
        // The decision the rules consumer reached is persisted on the change request.
        // A genuinely missing status falls back to Scrutiny so a fault never silently
        // produces an auto-approval or auto-rejection ticket.
        var status = changeRequest?.DecisionStatus ?? DecisionStatus.Scrutiny;
        var outcomeKey = string.IsNullOrEmpty(changeRequest?.DecisionOutcomeKey)
            ? message.WhatToChange
            : changeRequest.DecisionOutcomeKey;
        var matchedRuleId = changeRequest?.MatchedRuleId ?? string.Empty;
        return new Decision(status, outcomeKey, matchedRuleId, Array.Empty<string>());
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
                BrandId = _checkingExerciseSettings?.BrandId ?? 0,
                GroupId = _checkingExerciseSettings?.GroupId ?? 0,
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
        if (_checkingExerciseSettings is null)
        {
            return;
        }

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

    private void MapPupilFields(CreateTicketRequestDto dto, RequestDocument message)
    {
        if (_ticketFieldService is null)
        {
            return;
        }

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
        if (_blobServiceClient is null || _zendeskAttachmentService is null)
        {
            return;
        }

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
