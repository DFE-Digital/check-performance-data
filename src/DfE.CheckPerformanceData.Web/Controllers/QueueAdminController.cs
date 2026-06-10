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

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/queues")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var queues = await _queueAdminService.GetQueueDepthsAsync(cancellationToken);
        var dlqCount = await _queueAdminService.GetDlqCountAsync(cancellationToken);

        return View(new QueueIndexViewModel
        {
            Queues = queues,
            DeadLetterCount = dlqCount,
            RefreshedAtUtc = DateTime.UtcNow,
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
        });
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpPost("admin/queues/dlq/redrive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Redrive(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var validated = (ids ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).ToArray();
        if (validated.Length > 0)
            await _queueAdminService.RedriveAsync(validated, cancellationToken);

        return RedirectToAction(nameof(Dlq));
    }

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpPost("admin/queues/dlq/purge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Purge(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var validated = (ids ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).ToArray();
        if (validated.Length > 0)
            await _queueAdminService.PurgeAsync(validated, cancellationToken);

        return RedirectToAction(nameof(Dlq));
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
