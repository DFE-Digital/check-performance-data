using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class TitleController(ILogger<TitleController> logger, IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/Title.cshtml";

    [HttpGet("admin/windows/title")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");        
        WindowTitleEditItem model = new WindowTitleEditItem()
        {
            Title = draft?.Title ?? "New window",
            PostUrl = Url.Action("Submit", "Title"),
            CancelUrl = Url.Action("Index", "CancelCreation")
        };
        return View(PageView, model);
    }

    [HttpGet("admin/windows/{id:guid}/title")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        WindowTitleEditItem model = new WindowTitleEditItem
        {
            WindowId = window.Id,
            Title = window.Title,
            PostUrl = Url.Action("Update", "Title", new {id = window.Id}),
            CancelUrl = Url.Action("Index", "Summary", new { id = window.Id })
        };

        return View(PageView, model);
    }

    [HttpPost("admin/windows/title")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(WindowTitleEditItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft") ?? new CheckingWindowDraft();        
        if (!ModelState.IsValid)
        {
            return View(PageView, model);
        }

        draft.Title = model.Title;
        
        HttpContext.Session.SetObject("CheckingWindowDraft", draft);
        
        return Redirect(draft.NextController(Url));
    }

    [HttpPost("admin/windows/{id:guid}/title")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, WindowTitleEditItem model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(PageView, model);
        }
        
        if (id != model.WindowId)
        {
            return BadRequest();
        }

        CheckingWindowDto window = await windowService.GetByIdAsync(id, cancellationToken);
        if (window is null)
        {
            return NotFound();
        }
        
        window.Title = model.Title;
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { id = id});
    }
}