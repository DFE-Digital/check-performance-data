using System.Globalization;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Observability;
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

    // Test constructor for ticket-composition assertions: also wires the singleton
    // ticket-field service and settings so the built ticket's custom and pupil
    // fields can be exercised directly. Internal so DI never sees it.
    internal ZendeskConsumer(
        IQueueService queueService,
        IZendeskService zendeskService,
        IPortalDbContext dbContext,
        IZendeskTicketFieldService ticketFieldService,
        SchoolCheckingExerciseSettings checkingExerciseSettings)
        : base(queueService, Options.Create(new QueueOptions()), NullLogger<ZendeskConsumer>.Instance)
    {
        _zendeskService = zendeskService;
        _dbContext = dbContext;
        _ticketFieldService = ticketFieldService;
        _checkingExerciseSettings = checkingExerciseSettings;
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

    protected override MetricDescription? DescribeMetric(string payload, bool deadLettered)
    {
        var parsed = RequestDocumentParser.Parse(payload);
        return parsed is null
            ? null
            : new MetricDescription(MetricStages.TicketCreated, parsed.ReferenceNumber);
    }

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

        var changeRequest = await LoadChangeRequestAsync(message.ReferenceNumber, cancellationToken);

        // Check-before-create: a redelivery of a request whose ticket already exists
        // (CrmId persisted from a prior delivery) is a no-op, so at-least-once delivery never
        // double-creates a ticket — even across a worker restart. The durable guarantee below
        // does not depend on this read; it is the cheap path that avoids the claim round-trip.
        if (!string.IsNullOrEmpty(changeRequest?.CrmId))
        {
            return;
        }

        // Durably claim the "ticket created" transition before calling Zendesk. The claim flips
        // the status out of RulesProcessed in a single atomic UPDATE, so of two concurrent
        // deliveries that both saw CrmId == null only one flips a row — the loser gets zero rows
        // affected and skips without ever calling Zendesk, so no duplicate ticket is created.
        // A redelivery of this worker's own crashed attempt (status already Creating, CrmId
        // still null) re-claims and retries rather than stranding the request.
        if (!await TryClaimTicketCreationAsync(message.ReferenceNumber, cancellationToken))
        {
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

        // Record the ticket id only where it is still unset. The partial unique index on CrmId
        // is the final durable backstop against ever recording two ticket ids for one request.
        await _dbContext.ExecuteInTransactionAsync(async () =>
        {
            await _dbContext.ChangeRequests
                .Where(r => r.ReferenceNumber == message.ReferenceNumber && r.CrmId == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.CrmId, crmId)
                    .SetProperty(r => r.WorkerStatus, WorkerStatus.ZendeskTicketCreated),
                    cancellationToken);
        }, cancellationToken);

        _logger.LogInformation(
            "Created Zendesk ticket {TicketId} for Reference={Reference} (Decision={Status}, Rule={Rule}).",
            response.Ticket.Id, message.ReferenceNumber, decision.Status, decision.MatchedRuleId);
    }

    // Atomically flips the request from RulesProcessed into the in-progress "creating" state.
    // Returns true to the single delivery that wins the flip. The flip is exclusive: Postgres
    // takes a row lock on the matching row, so of two concurrent deliveries the second re-checks
    // its WHERE against the winner's committed row — which is no longer RulesProcessed — and
    // matches zero rows. The loser therefore skips without ever calling Zendesk, so concurrent
    // redelivery cannot create a duplicate ticket. CrmId == null keeps an already-ticketed
    // request (whose status is ZendeskTicketCreated) from ever being re-claimed.
    private async Task<bool> TryClaimTicketCreationAsync(string referenceNumber, CancellationToken cancellationToken)
    {
        var claimed = 0;
        await _dbContext.ExecuteInTransactionAsync(async () =>
        {
            claimed = await _dbContext.ChangeRequests
                .Where(r => r.ReferenceNumber == referenceNumber
                    && r.CrmId == null
                    && r.WorkerStatus == WorkerStatus.RulesProcessed)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.WorkerStatus, WorkerStatus.ZendeskTicketCreating),
                    cancellationToken);
        }, cancellationToken);

        return claimed == 1;
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
        var status = changeRequest?.Outcome ?? DecisionStatus.Scrutiny;
        var outcomeKey = string.IsNullOrEmpty(changeRequest?.OutcomeKey)
            ? message.RequestTypeCode
            : changeRequest.OutcomeKey;
        var matchedRuleId = changeRequest?.MatchedRuleId ?? string.Empty;
        return new Decision(status, outcomeKey, matchedRuleId, Array.Empty<string>());
    }

    internal CreateTicketRequestDto BuildTicket(RequestDocument message, Decision decision)
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

        AddDecisionCustomFields(dto, decision);
        MapPupilFields(dto, message);
        AddRequestFields(dto, message);
        AddCycleFields(dto, message);
        MapKeyStageField(dto, message);
        AddCorrectionFields(dto, message);
        MapLdsMatchedPupilIdField(dto, message);
        MapDfeEstablishmentNumberField(dto, message);
        MapAdmissionDateField(dto, message);
        AddDecisionReasonField(dto, decision);
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

    private void AddDecisionCustomFields(CreateTicketRequestDto dto, Decision decision)
    {
        
        if (_ticketFieldService is null)
        {
            return;
        }
        
        dto.Ticket.CustomFields ??= new List<CustomFieldDto>();
        var decisionStatusCustomFieldId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionStatusName);
        if (decisionStatusCustomFieldId.HasValue && decisionStatusCustomFieldId.Value > 0)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = decisionStatusCustomFieldId,
                Value = _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionStatusName,decision.Status.ToString())
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

    // FR-002 / FR-003: DCI Ref (CYPMD) from the request reference number; Age (CYPMD) from
    // the pupil's age. Omitted when the field ID is unset (FR-014).
    private void AddRequestFields(CreateTicketRequestDto dto, RequestDocument message)
    {
        if (_ticketFieldService is null)
        {
            return;
        }

        dto.Ticket.CustomFields ??= new List<CustomFieldDto>();

        var dciRefId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DciRefCypmdName);
        if (dciRefId.HasValue && !string.IsNullOrEmpty(message.ReferenceNumber))
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = dciRefId.Value,
                Value = message.ReferenceNumber,
            });
        }

        var ageId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.AgeCypmdName);
        if (ageId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = ageId.Value,
                Value = message.Pupil.Age.ToString(CultureInfo.InvariantCulture),
            });
        }
    }

    // FR-004 / FR-005: cycle year and month from the submission instant.
    private void AddCycleFields(CreateTicketRequestDto dto, RequestDocument message)
    {
        if (_ticketFieldService is null)
        {
            return;
        }

        dto.Ticket.CustomFields ??= new List<CustomFieldDto>();

        var cycleYearId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CycleYearName);
        if (cycleYearId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = cycleYearId.Value,
                Value = message.SubmittedAt.Year.ToString(CultureInfo.InvariantCulture),
            });
        }

        var cycleMonthId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CycleMonthName);
        if (cycleMonthId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = cycleMonthId.Value,
                Value = message.SubmittedAt.Month.ToString(CultureInfo.InvariantCulture),
            });
        }
    }

    // FR-006: key stage from the checking window type via the field's option map. Omitted
    // when no option exists for the window (e.g. KS4/Post16 until confirmed) — FR-014.
    private void MapKeyStageField(CreateTicketRequestDto dto, RequestDocument message)
    {
        if (_ticketFieldService is null)
        {
            return;
        }

        var keyStageId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.KeyStageName);
        if (keyStageId.HasValue && !string.IsNullOrEmpty(message.CheckingWindowType))
        {
            var keyStageValue = _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.KeyStageName, message.CheckingWindowType);
            if (!string.IsNullOrEmpty(keyStageValue))
            {
                dto.Ticket.CustomFields ??= new List<CustomFieldDto>();
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = keyStageId.Value,
                    Value = keyStageValue,
                });
            }
            else
            {
                _logger.LogWarning("No key stage option for checking window type '{WindowType}', skipping field.", message.CheckingWindowType);
            }
        }
    }

    // FR-009 / FR-010 / FR-011: correction type (31_ for Remove requests), correction reason
    // (via the removal-reason map) and reason for removal. Omitted for non-Remove requests and
    // when a removal reason maps to no correction code (logged for review, FR-014 edge case).
    private void AddCorrectionFields(CreateTicketRequestDto dto, RequestDocument message)
    {
        if (_ticketFieldService is null)
        {
            return;
        }

        // Only Remove requests carry correction type/reason and reason for removal this phase.
        if (!message.RequestTypeCode.StartsWith("Remove", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        dto.Ticket.CustomFields ??= new List<CustomFieldDto>();

        var correctionTypeId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionTypeName);
        if (correctionTypeId.HasValue)
        {
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = correctionTypeId.Value,
                Value = ZendeskTicketFieldOptions.CorrectionType.Correction31,
            });
        }

        // The removal reason is the RequestTypeCode suffix after "Remove - ", e.g. "pupil-died".
        var removalReason = message.RequestTypeCode.Length > "Remove".Length
            ? message.RequestTypeCode["Remove".Length..].TrimStart('-', ' ')
            : string.Empty;

        if (string.IsNullOrEmpty(removalReason))
        {
            return;
        }

        var correctionReasonId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionReason31Name);
        if (correctionReasonId.HasValue)
        {
            var correctionReasonValue = _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.CorrectionReason31Name, removalReason);
            if (!string.IsNullOrEmpty(correctionReasonValue))
            {
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = correctionReasonId.Value,
                    Value = correctionReasonValue,
                });
            }
            else
            {
                _logger.LogWarning("No correction reason (31) code for removal reason '{RemovalReason}', skipping field.", removalReason);
            }
        }

        var reasonForRemovalId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName);
        if (reasonForRemovalId.HasValue)
        {
            var reasonForRemovalValue = _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.ReasonForRemovalName, removalReason);
            if (!string.IsNullOrEmpty(reasonForRemovalValue))
            {
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = reasonForRemovalId.Value,
                    Value = reasonForRemovalValue,
                });
            }
            else
            {
                _logger.LogWarning("No reason for removal option for removal reason '{RemovalReason}', skipping field.", removalReason);
            }
        }
    }

    // FR-007: LDS matched pupil ID from the matched record, falling back to the submitted
    // pupil. 0 = not supplied, so the field is omitted (FR-014).
    private void MapLdsMatchedPupilIdField(CreateTicketRequestDto dto, RequestDocument message)
    {
        if (_ticketFieldService is null)
        {
            return;
        }

        var fieldId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.LdsMatchedPupilIdName);
        var matchRef = (message.MatchedPupil ?? message.Pupil).MatchRef;
        if (fieldId.HasValue && matchRef > 0)
        {
            dto.Ticket.CustomFields ??= new List<CustomFieldDto>();
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = fieldId.Value,
                Value = matchRef.ToString(CultureInfo.InvariantCulture),
            });
        }
    }

    // FR-008: DfE Establishment Number from the requesting school's LAESTAB. Empty = not
    // supplied, so the field is omitted (FR-014).
    private void MapDfeEstablishmentNumberField(CreateTicketRequestDto dto, RequestDocument message)
    {
        if (_ticketFieldService is null)
        {
            return;
        }

        var fieldId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DfeEstablishmentNumberName);
        if (fieldId.HasValue && !string.IsNullOrEmpty(message.School.Laestab))
        {
            dto.Ticket.CustomFields ??= new List<CustomFieldDto>();
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = fieldId.Value,
                Value = message.School.Laestab,
            });
        }
    }

    // FR-012: admission date from the pupil record's ENTRYDAT (the same date the portal shows
    // as its "Admission date" column). ENTRYDAT is supplier-defined format, normalised to ISO
    // yyyy-MM-dd. Omitted + logged when absent/unparseable (FR-014).
    private void MapAdmissionDateField(CreateTicketRequestDto dto, RequestDocument message)
    {
        if (_ticketFieldService is null)
        {
            return;
        }

        var fieldId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.AdmissionDateName);
        if (fieldId.HasValue)
        {
            var isoDate = PupilDateFormatter.ToIsoDate(message.Pupil.EntryDate);
            if (!string.IsNullOrWhiteSpace(isoDate))
            {
                dto.Ticket.CustomFields ??= new List<CustomFieldDto>();
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = fieldId.Value,
                    Value = isoDate,
                });
            }
            else
            {
                _logger.LogWarning("Admission date absent for Reference={Reference}, skipping field.", message.ReferenceNumber);
            }
        }
    }

    // FR-013: Decision Reason - Approved from the rules outcome key via the curated tagger map.
    // Gated to auto-approved decisions (the field records the reason an APPROVED decision met
    // its criteria); auto-rejected decisions additionally populate when the
    // PopulateDecisionReasonForAutoRejected setting is enabled; scrutiny never does. Omitted
    // when the decision is excluded or has no mapping (FR-014).
    private void AddDecisionReasonField(CreateTicketRequestDto dto, Decision decision)
    {
        if (_ticketFieldService is null)
        {
            return;
        }

        var fieldId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonApprovedName);
        if (!fieldId.HasValue)
        {
            return;
        }

        var includeRejected = _ticketFieldService.PopulateDecisionReasonForAutoRejected;
        var isIncludedStatus = decision.Status == DecisionStatus.AutoApproved
            || (includeRejected && decision.Status == DecisionStatus.AutoRejected);
        if (!isIncludedStatus)
        {
            _logger.LogDebug(
                "Decision Reason - Approved not populated for {Status} decision (AutoRejected is included only when {Setting} is enabled).",
                decision.Status, nameof(ZendeskTicketFieldSettings.PopulateDecisionReasonForAutoRejected));
            return;
        }

        var option = _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonApprovedName, decision.OutcomeKey);
        if (!string.IsNullOrEmpty(option))
        {
            dto.Ticket.CustomFields ??= new List<CustomFieldDto>();
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = fieldId.Value,
                Value = option,
            });
        }
        else
        {
            _logger.LogWarning("No Decision Reason - Approved option for outcome '{OutcomeKey}', skipping field.", decision.OutcomeKey);
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
