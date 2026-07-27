using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Compile-stub for the admin messages inbox controller. The real implementation is
// added by the follow-up plan; this stub exists only so tests written against the
// controller type compile.
[RequireAdminSection(AdminNavKeys.MessagesInbox)]
[Route("admin/Messages")]
public sealed class MessagesController : Controller
{
    public MessagesController(
        ISearchMessageService messages,
        ISettingService settings,
        ICurrentUserService? currentUserService = null)
    {
        _ = messages; _ = settings; _ = currentUserService;
    }

    [HttpGet("Inbox")]
    public Task<IActionResult> Inbox(
        string? sort, string? dir, string? filter, int page = 1, CancellationToken ct = default)
        => throw new NotImplementedException("Messages inbox not yet implemented.");

    [HttpGet("Inbox/{id:long}")]
    public Task<IActionResult> Detail(long id, CancellationToken ct = default)
        => throw new NotImplementedException("Messages inbox not yet implemented.");

    [HttpPost("Inbox/{id:long}/MarkRead")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> MarkRead(long id, CancellationToken ct = default)
        => throw new NotImplementedException("Messages inbox not yet implemented.");
}
