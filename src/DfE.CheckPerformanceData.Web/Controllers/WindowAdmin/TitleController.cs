using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class TitleController(ILogger<TitleController> logger, IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/Title.cshtml";

    [ActionName("NewTitle")]
    [HttpGet("admin/windows/title")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");        
        WindowTitleEditItem model = new WindowTitleEditItem()
        {
            Title = draft?.Title ?? "New window",
            PostUrl = "/admin/windows/title"
        };
        return View(PageView, model);
    }

    [ActionName("EditTitle")]
    [HttpGet("admin/windows/{id:guid}/title")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
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
            PostUrl = $"/admin/windows/{window.Id}/title"
        };

        return View(PageView, model);
    }

    [HttpPost("admin/windows/title")]
    public async Task<IActionResult> Index(WindowTitleEditItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft") ?? new CheckingWindowDraft();        
        if (ModelState.ErrorCount > 0)
        {
            return View(PageView, model);
        }

        draft?.Title = model.Title;
        
        HttpContext.Session.SetObject("CheckingWindowDraft", draft);
        
        return RedirectToAction("New", draft?.NextController());
    }

    [HttpPost("admin/windows/{id:guid}/title")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Guid id, WindowTitleEditItem model, CancellationToken cancellationToken)
    {
        if (ModelState.ErrorCount > 0)
        {
            return View(PageView, model);
        }
        
        if (id != model.WindowId)
        {
            return BadRequest();
        }

        CheckingWindowDto window = await windowService.GetByIdAsync(id, cancellationToken);
        window.Title = model?.Title;
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { Id = id});
    }
}