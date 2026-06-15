using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Read-only and management surface over the Postgres queue and dead-letter queue.
// Gated by the cypmd_admin role on every action; destructive verbs require antiforgery.
// Payloads are redacted by default; the full payload is only rendered when the
// Dlq:FullPayloadEnabled setting is on, and every full-payload view is audited.
public sealed class QueueAdminController : Controller
{
    private readonly IQueueAdminService _queueAdminService;
    private readonly PayloadRedactor _redactor;
    private readonly ISettingService? _settingService;
    private readonly IPortalDbContext? _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    public QueueAdminController(
        IQueueAdminService queueAdminService,
        PayloadRedactor? redactor = null,
        ISettingService? settingService = null,
        IPortalDbContext? dbContext = null,
        ICurrentUserService? currentUserService = null)
    {
        _queueAdminService = queueAdminService;
        _redactor = redactor ?? new PayloadRedactor();
        _settingService = settingService;
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    private const int TopMessageCount = 5;

    // The working queues that can be listed in full, plus their display names. This is also the
    // server-side allow-list for the per-queue view-all action: a queue name not in here is
    // rejected before any read, so the route parameter can never reach a query for an arbitrary
    // table value.
    private static readonly IReadOnlyDictionary<string, string> WorkingQueues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [QueueOptions.RulesEngineQueue] = "Rules engine queue",
            [QueueOptions.ZendeskQueue] = "Zendesk queue",
        };

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/queues")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var depths = await _queueAdminService.GetQueueDepthsAsync(cancellationToken);
        var dlqCount = await _queueAdminService.GetDlqCountAsync(cancellationToken);

        var panels = new List<QueuePanelViewModel>();

        // One panel per working queue, in a stable display order, each carrying its top-5.
        foreach (var (queueName, displayName) in WorkingQueues)
        {
            var depth = depths.FirstOrDefault(d => d.QueueName == queueName);
            var top = await _queueAdminService.GetTopMessagesAsync(queueName, TopMessageCount, cancellationToken);

            panels.Add(new QueuePanelViewModel
            {
                QueueName = queueName,
                DisplayName = displayName,
                Count = depth?.Depth ?? 0,
                OldestMessageAge = depth?.OldestMessageAge,
                TopMessages = top,
            });
        }

        // The dead-letter queue is rendered through the same panel; its actions point at the
        // existing dead-letter surface rather than the working-queue view-all.
        panels.Add(new QueuePanelViewModel
        {
            QueueName = "dead-letter",
            DisplayName = "Dead-letter queue",
            Count = dlqCount,
            IsDeadLetter = true,
        });

        return View(new QueueIndexViewModel
        {
            Panels = panels,
            RefreshedAtUtc = DateTime.UtcNow,
        });
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/queues/list/{queueName}")]
    public async Task<IActionResult> QueueList(string queueName, CancellationToken cancellationToken = default)
    {
        if (!WorkingQueues.TryGetValue(queueName, out var displayName))
            return NotFound();

        var messages = await _queueAdminService.GetQueueMessagesAsync(queueName, cancellationToken);

        return View(new QueueListViewModel
        {
            QueueName = queueName,
            DisplayName = displayName,
            Messages = messages,
        });
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/queues/list/{queueName}/{id:guid}")]
    public async Task<IActionResult> WorkingMessage(string queueName, Guid id, CancellationToken cancellationToken = default)
    {
        if (!WorkingQueues.TryGetValue(queueName, out _))
            return NotFound();

        var message = await _queueAdminService.GetMessageDetailAsync(queueName, id, cancellationToken);
        if (message is null)
            return NotFound();

        var fullPayloadEnabled = await IsFullPayloadEnabledAsync();

        string payload;
        bool redacted;
        if (fullPayloadEnabled)
        {
            payload = message.Payload;
            redacted = false;
            await WriteFullPayloadViewAuditAsync(message.Id, cancellationToken);
        }
        else
        {
            payload = _redactor.Redact(message.Payload);
            redacted = true;
        }

        return View(new QueueMessageViewModel
        {
            Id = message.Id,
            QueueName = message.QueueName,
            Attempts = message.Attempts,
            EnqueuedAtUtc = message.EnqueuedAtUtc,
            Payload = payload,
            IsRedacted = redacted,
            FullPayloadAvailable = fullPayloadEnabled,
            // Extracted from the raw payload regardless of redaction: the reference is the
            // redaction-safe journey key, not a pupil identifier.
            ReferenceNumber = QueuePayloadReference.TryExtract(message.Payload),
        });
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/queues/dlq")]
    public async Task<IActionResult> Dlq(CancellationToken cancellationToken = default)
    {
        var messages = await _queueAdminService.GetDlqMessagesAsync(cancellationToken);

        return View(new DlqListViewModel { Messages = messages });
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/queues/dlq/{id:guid}")]
    public async Task<IActionResult> Message(Guid id, CancellationToken cancellationToken = default)
    {
        var message = await _queueAdminService.GetDlqMessageAsync(id, cancellationToken);
        if (message is null)
            return NotFound();

        var fullPayloadEnabled = await IsFullPayloadEnabledAsync();

        string payload;
        bool redacted;
        if (fullPayloadEnabled)
        {
            payload = message.Payload;
            redacted = false;
            await WriteFullPayloadViewAuditAsync(message.Id, cancellationToken);
        }
        else
        {
            payload = _redactor.Redact(message.Payload);
            redacted = true;
        }

        return View(new DlqMessageViewModel
        {
            Id = message.Id,
            QueueName = message.QueueName,
            Attempts = message.Attempts,
            Reason = message.Reason,
            DeadLetteredAtUtc = message.DeadLetteredAtUtc,
            Payload = payload,
            IsRedacted = redacted,
            FullPayloadAvailable = fullPayloadEnabled,
            ReferenceNumber = QueuePayloadReference.TryExtract(message.Payload),
        });
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpPost("admin/queues/dlq/redrive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Redrive(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var validated = Validate(ids);
        if (validated.Length == 0)
            return RedirectToAction(nameof(Dlq));

        try
        {
            // Audit and requeue commit together: the forensic record of the redrive and the
            // mutation it describes never diverge under a failure or crash.
            await RunAuditedActionAsync(
                "Redrive",
                validated,
                () => _queueAdminService.RedriveAsync(validated, cancellationToken),
                cancellationToken);
            SetResult($"Requeued {Count(validated.Length, "message")}.");
        }
        catch (InvalidOperationException ex)
        {
            SetResult($"Could not redrive: {ex.Message}");
        }

        return RedirectToAction(nameof(Dlq));
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpPost("admin/queues/dlq/{id:guid}/redrive")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> RedriveMessage(Guid id, CancellationToken cancellationToken = default) =>
        Redrive(new[] { id }, cancellationToken);

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpPost("admin/queues/dlq/purge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Purge(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var validated = Validate(ids);
        if (validated.Length == 0)
            return RedirectToAction(nameof(Dlq));

        // Purge is irreversible, so its forensic record must name the actor. If an audited
        // context is wired but the acting user is unknown, refuse rather than record an
        // anonymous purge of data-bearing rows.
        if (!HasKnownActorForAudit())
        {
            SetResult("Could not purge: the acting user could not be determined.");
            return RedirectToAction(nameof(Dlq));
        }

        try
        {
            // The audit and the purge commit together. The audit is still written first within
            // the transaction so it survives the deletion, and the shared transaction makes the
            // pair atomic — a purge can never land without its forensic record, nor vice versa.
            await RunAuditedActionAsync(
                "Purge",
                validated,
                () => _queueAdminService.PurgeAsync(validated, cancellationToken),
                cancellationToken);
            SetResult($"Purged {Count(validated.Length, "message")}. The audit record is retained.");
        }
        catch (InvalidOperationException ex)
        {
            SetResult($"Could not purge: {ex.Message}");
        }

        return RedirectToAction(nameof(Dlq));
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpPost("admin/queues/dlq/{id:guid}/purge")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> PurgeMessage(Guid id, CancellationToken cancellationToken = default) =>
        Purge(new[] { id }, cancellationToken);

    // Drops anything that is not a real message id (Guid.Empty / unknown placeholders) so a
    // tampered form can never act on an unintended id. The dead-letter store itself only acts
    // on ids it actually holds, giving a second server-side allow-list.
    private static Guid[] Validate(IReadOnlyCollection<Guid>? ids) =>
        (ids ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().ToArray();

    private static string Count(int n, string noun) =>
        n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    // A destructive action may proceed only when its audit can name the actor. With no audited
    // context there is nothing to audit (bare construction), so it is allowed; with an audited
    // context the acting user id must be present.
    private bool HasKnownActorForAudit() =>
        _dbContext is null || !string.IsNullOrWhiteSpace(_currentUserService?.UserId);

    // Runs a destructive queue action and its forensic audit as one unit. When an audited
    // context is available the audit write and the mutation share a single transaction, so they
    // commit or roll back together and the trail can never diverge from the deletion it records.
    // The audit is written first within the transaction so it still survives the purged rows.
    private async Task RunAuditedActionAsync(
        string action,
        IReadOnlyCollection<Guid> ids,
        Func<Task> mutate,
        CancellationToken cancellationToken)
    {
        if (_dbContext is null)
        {
            // Bare construction (no audited context): run the mutation directly. There is no
            // forensic context to enrol, so there is nothing to make atomic.
            await mutate();
            return;
        }

        await _dbContext.ExecuteInTransactionAsync(async () =>
        {
            await WriteActionAuditAsync(action, ids, cancellationToken);
            await mutate();
        }, cancellationToken);
    }

    // Records an immutable forensic entry (who/when/what) for the destructive admin action.
    // Written through the audited context so it carries the acting user and is retained
    // independently of the messages it names.
    private async Task WriteActionAuditAsync(string action, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (_dbContext is null)
            return;

        _dbContext.AuditEntries.Add(new AuditEntry
        {
            EntityType = "DlqMessage",
            EntityId = string.Join(",", ids),
            Action = action,
            Timestamp = DateTime.UtcNow,
            UserId = _currentUserService?.UserId,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void SetResult(string message)
    {
        // TempData is only available when an MVC TempData provider is wired (it is at
        // runtime; it is not under bare unit-test construction) — guard so the success
        // banner never NREs the action.
        if (TempData is not null)
            TempData["QueueAdminResult"] = message;
    }

    private async Task<bool> IsFullPayloadEnabledAsync()
    {
        if (_settingService is null)
            return false;

        var value = await _settingService.GetValueAsync(SettingKeys.DlqFullPayloadEnabled);
        return bool.TryParse(value, out var enabled) && enabled;
    }

    private async Task WriteFullPayloadViewAuditAsync(Guid messageId, CancellationToken cancellationToken)
    {
        if (_dbContext is null)
            return;

        _dbContext.AuditEntries.Add(new AuditEntry
        {
            EntityType = "DlqMessage",
            EntityId = messageId.ToString(),
            Action = "ViewFullPayload",
            Timestamp = DateTime.UtcNow,
            UserId = _currentUserService?.UserId,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
