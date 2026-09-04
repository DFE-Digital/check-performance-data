using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>
/// AB#298317: edits a window's next-opportunity date from the Summary, mirroring
/// <see cref="TurnaroundCommitmentController"/>. Not a wizard step: like the turnaround
/// commitment it is set after the window exists.
/// </summary>
public sealed class NextOpportunityController(IWindowService windowService) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/NextOpportunity.cshtml";

    [HttpGet("admin/windows/{id:guid}/next-opportunity")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        var model = new WindowNextOpportunityEditItem
        {
            WindowId = window.Id,
            NextOpportunity = window.NextOpportunity
        };
        Decorate(model, id);

        return View(PageView, model);
    }

    [HttpPost("admin/windows/{id:guid}/next-opportunity")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, WindowNextOpportunityEditItem model, CancellationToken cancellationToken)
    {
        // AB#298317: NextOpportunity has no [Required] (blank = "not set" is a valid answer), so
        // the GOV.UK date-input binder — which only raises its own "must be a real date" error for
        // a required field — silently binds an impossible date (e.g. 31 February) to null rather
        // than rejecting it. That reads as "the admin cleared it", which is wrong: they typed
        // something. Rejected here instead, by checking whether any date part was actually posted.
        // Gated on ModelState already being valid: skips the Request.Form read entirely when the
        // binder (or something else) has already added an error, which is also what keeps this
        // check from running against a request that never touched a real HttpContext.
        if (ModelState.IsValid && model.NextOpportunity is null && WasDatePosted())
        {
            ModelState.AddModelError(nameof(WindowNextOpportunityEditItem.NextOpportunity), "Next opportunity must be a real date");
        }

        if (!ModelState.IsValid)
        {
            // The urls are not posted back, so a redisplayed page has to be given them again.
            Decorate(model, id);
            return View(PageView, model);
        }

        if (id != model.WindowId)
        {
            return BadRequest();
        }

        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
        {
            return NotFound();
        }

        // Stored as the date at midnight: only the month and year are ever shown to schools.
        window.NextOpportunity = model.NextOpportunity?.Date;
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { id });
    }

    private void Decorate(WindowNextOpportunityEditItem model, Guid id)
    {
        model.PostUrl = Url.Action("Update", "NextOpportunity", new { id });
        model.CancelUrl = Url.Action("Index", "Summary", new { id });
    }

    // True when at least one of the date-input's Day/Month/Year fields was actually filled in —
    // distinguishes "left blank on purpose" (all empty, a valid answer) from "typed something that
    // did not parse" (at least one non-empty, which the binder otherwise swallows silently).
    private bool WasDatePosted() =>
        !string.IsNullOrWhiteSpace(Request.Form["NextOpportunity.Day"])
        || !string.IsNullOrWhiteSpace(Request.Form["NextOpportunity.Month"])
        || !string.IsNullOrWhiteSpace(Request.Form["NextOpportunity.Year"]);
}
