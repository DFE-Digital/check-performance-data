using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Web.Models.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// The admin-only management surface for share/wallboard tokens. Kept off the Observability
// controller so the dashboard file is not shared across waves. Every action is gated by the
// cypmd_admin role, so only an authenticated admin can mint or revoke a token. The plaintext token
// is shown ONCE on generation (only its hash is stored), surfaced via TempData so it survives the
// post-redirect-get without being persisted.
[Authorize(Roles = WikiConstants.AdminRole)]
public sealed class ShareAdminController : Controller
{
    private readonly IShareTokenService _tokens;
    private readonly ICurrentUserService? _currentUser;

    public ShareAdminController(IShareTokenService tokens, ICurrentUserService? currentUser = null)
    {
        _tokens = tokens;
        _currentUser = currentUser;
    }

    [HttpGet("admin/share")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var tokens = await _tokens.ListAsync(cancellationToken);

        return View(new ShareAdminViewModel
        {
            Tokens = tokens,
            NewToken = TempData["ShareTokenPlaintext"] as string,
            NewTokenSurface = TempData["ShareTokenSurface"] as string,
        });
    }

    [HttpPost("admin/share/generate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateShareToken(
        string label,
        ShareSurface surface,
        CancellationToken cancellationToken = default)
    {
        var safeLabel = string.IsNullOrWhiteSpace(label) ? "Untitled" : label.Trim();
        var createdBy = _currentUser?.UserId ?? "admin";

        var plaintext = await _tokens.GenerateAsync(safeLabel, surface, createdBy, cancellationToken);

        // Shown once: the plaintext is never stored, so it is surfaced here and only here.
        if (TempData is not null)
        {
            TempData["ShareTokenPlaintext"] = plaintext;
            TempData["ShareTokenSurface"] = surface.ToString();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("admin/share/{id:guid}/revoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeShareToken(Guid id, CancellationToken cancellationToken = default)
    {
        await _tokens.RevokeAsync(id, cancellationToken);

        if (TempData is not null)
            TempData["ShareAdminResult"] = "Token revoked. Any link using it now returns a 404.";

        return RedirectToAction(nameof(Index));
    }
}
