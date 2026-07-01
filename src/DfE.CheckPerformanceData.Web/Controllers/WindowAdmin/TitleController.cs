using System.Text.Json;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class TitleController(ILogger<TitleController> logger, IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/Title.cshtml";

    [HttpGet("admin/windows/title")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        WindowTitleEditItem model = new WindowTitleEditItem() { Title = "New window" };
        return View(PageView, model);
    }

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
            Title = window.Title
        };

        return View(PageView, model);
    }

    [HttpPost("admin/windows/title")]
    public async Task<IActionResult> Index(WindowTitleEditItem model, CancellationToken cancellationToken)
    {
        if (ModelState.ErrorCount > 0)
        {
            return View(PageView, model);
        }
        
        CheckingWindowDraft draft = new CheckingWindowDraft
        {
            Title = model.Title
        };
        HttpContext.Session.SetString(
            "CheckingWindowDraft",
            JsonSerializer.Serialize(draft));
            
        throw new NotImplementedException("Will redirect to required controllers");
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
        window.Title = model.Title;
        await windowService.UpdateAsync(window, cancellationToken);

        return Redirect($"/admin/windows/summary/{id}");
    }
}