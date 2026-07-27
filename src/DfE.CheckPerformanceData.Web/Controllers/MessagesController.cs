using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Admin messages inbox. Reads run against the messages service; writes (mark-read) are
// idempotent and global — a second admin's click is a no-op update that leaves the
// first admin's attribution on the row. Every action is gated by the messages-inbox
// section grant via the class-level attribute; the underlying seeder has an entry for
// messages-inbox, so an out-of-the-box admin role reaches this surface immediately.
[RequireAdminSection(AdminNavKeys.MessagesInbox)]
[Route("admin/Messages")]
public sealed class MessagesController : Controller
{
    private const string DefaultSort = "submittedAt";
    private const string DefaultDir = "desc";
    private const int DefaultPageSize = 20;

    private readonly ISearchMessageService _messages;
    private readonly ISettingService _settings;
    private readonly ICurrentUserService? _currentUserService;

    public MessagesController(
        ISearchMessageService messages,
        ISettingService settings,
        ICurrentUserService? currentUserService = null)
    {
        _messages = messages;
        _settings = settings;
        _currentUserService = currentUserService;
    }

    [HttpGet("Inbox")]
    public async Task<IActionResult> Inbox(
        string? sort,
        string? dir,
        string? filter,
        int page = 1,
        CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.MessagesInbox;
        ViewData["Title"] = "Messages inbox";

        if (page < 1) page = 1;
        var pageSize = await ResolvePageSizeAsync();
        var (sortField, sortKey) = ResolveSort(sort);
        var (sortDesc, dirKey) = ResolveDir(dir);

        var result = await _messages.GetInboxAsync(sortField, sortDesc, filter, page, pageSize, ct);

        return View("~/Views/Admin/Messages/Inbox.cshtml", new MessagesInboxViewModel
        {
            Rows = result.Rows,
            TotalCount = result.TotalCount,
            Page = page,
            PageSize = pageSize,
            Sort = sortKey,
            Dir = dirKey,
            Filter = filter,
        });
    }

    [HttpGet("Inbox/{id:long}")]
    public async Task<IActionResult> Detail(long id, CancellationToken ct = default)
    {
        ViewData["AdminActiveKey"] = AdminNavKeys.MessagesInbox;
        ViewData["Title"] = "Message";

        var detail = await _messages.GetByIdAsync(id, ct);
        if (detail is null)
        {
            return NotFound();
        }

        return View("~/Views/Admin/Messages/Detail.cshtml",
            new MessagesDetailViewModel { Message = detail });
    }

    [HttpPost("Inbox/{id:long}/MarkRead")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct = default)
    {
        // Attribution is on the row itself (read_by_admin_sub + read_at_utc). The
        // mark-read stamp uses the acting admin's DfE-Sign-In sub; falls back to
        // "unknown" only when the current-user service is not wired (bare unit-scope
        // construction with no auth pipeline). Under runtime auth the sub is always
        // present.
        var adminSub = _currentUserService?.UserId;
        if (string.IsNullOrWhiteSpace(adminSub)) adminSub = "unknown";

        await _messages.MarkReadAsync(id, adminSub, DateTime.UtcNow, ct);

        // Redirect back to the detail view so the admin sees the row's new state and
        // can immediately click through to the session drill-in.
        return RedirectToAction(nameof(Detail), new { id });
    }

    private async Task<int> ResolvePageSizeAsync()
    {
        var size = await _settings.GetIntAsync(SettingKeys.CmsPageLength);
        return size > 0 ? size : DefaultPageSize;
    }

    // Accepts submittedAt (default) or isRead. Anything else falls back to submittedAt so
    // a stale query string still renders a valid inbox.
    private static (SearchMessageSortField Field, string Key) ResolveSort(string? sort)
    {
        return sort switch
        {
            "isRead" => (SearchMessageSortField.IsRead, "isRead"),
            _        => (SearchMessageSortField.SubmittedAt, DefaultSort),
        };
    }

    // Accepts asc or desc (default). Anything else falls back to desc so the newest-first
    // default is preserved on a malformed query string.
    private static (bool SortDesc, string Key) ResolveDir(string? dir)
    {
        return dir switch
        {
            "asc" => (false, "asc"),
            _     => (true, DefaultDir),
        };
    }
}
