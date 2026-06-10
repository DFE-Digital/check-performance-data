using DfE.CheckPerformanceData.Application.Queue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Read-only and management surface over the Postgres queue and dead-letter queue.
// Gated by the cypmd_admin role on every action; destructive verbs require antiforgery.
public sealed class QueueAdminController(IQueueAdminService queueAdminService) : Controller
{
    private readonly IQueueAdminService _queueAdminService = queueAdminService;

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/queues")]
    public Task<IActionResult> Index() => throw new NotImplementedException();

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/queues/dlq")]
    public Task<IActionResult> Dlq() => throw new NotImplementedException();

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpGet("admin/queues/dlq/{id:guid}")]
    public Task<IActionResult> Message(Guid id) => throw new NotImplementedException();

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpPost("admin/queues/dlq/redrive")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Redrive(IReadOnlyCollection<Guid> ids) => throw new NotImplementedException();

    [Authorize(Roles = WikiConstants.AdminRole)]
    [HttpPost("admin/queues/dlq/purge")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Purge(IReadOnlyCollection<Guid> ids) => throw new NotImplementedException();
}
