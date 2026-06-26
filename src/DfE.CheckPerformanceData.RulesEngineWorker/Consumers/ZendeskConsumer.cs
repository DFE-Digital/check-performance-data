using System.Globalization;
using System.Text;
using Azure.Storage.Blobs;
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
        dto.Ticket.CustomFields ??= new List<CustomFieldDto>();
        if (_ticketFieldService != null)
        {
            var decisionStatusCustomFieldId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionStatusName);
            dto.Ticket.CustomFields.Add(new CustomFieldDto
            {
                Id = decisionStatusCustomFieldId,
                Value = decision.Status.ToString(),
            });
            
            var outcomeKeyFieldId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.RulesEngineOutcomeKeyName);
            if(outcomeKeyFieldId.HasValue && outcomeKeyFieldId.Value > 0)
            {
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = outcomeKeyFieldId,
                    Value = decision.OutcomeKey,
                });
            }

            var matchedRuleIdCustomFieldId = _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.RulesEngineMatchedRuleIdName);
            if (matchedRuleIdCustomFieldId.HasValue && matchedRuleIdCustomFieldId.Value > 0)
            {
                dto.Ticket.CustomFields.Add(new CustomFieldDto
                {
                    Id = matchedRuleIdCustomFieldId,
                    Value = decision.MatchedRuleId,
                });
            }
        }

        if (_checkingExerciseSettings is null)
        {
            return;
        }

        

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
