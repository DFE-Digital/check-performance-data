using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class TitleController(ILogger<TitleController> logger, IWindowService windowService): Controller
{
    [HttpGet("admin/windows/{id:guid}/title")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        var model = new WindowTitleEditItem
        {
            WindowId = window.Id,
            Title = window.Title
        };

        return View("~/Views/WindowAdmin/Title.cshtml", model);
    }
    
    [HttpPost("admin/windows/{id:guid}/title")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Guid id, WindowTitleEditItem model, CancellationToken cancellationToken)
    {
        if (id != model.WindowId)
        {
            return BadRequest();
        }

        if (string.IsNullOrWhiteSpace(model.Title))
        {
            ModelState.AddModelError(nameof(model.Title), "Enter a title");
            return View("~/Views/WindowAdmin/Title.cshtml", model);
        }

        CheckingWindowDto window = await windowService.GetByIdAsync(id, cancellationToken);
        window.Title = model.Title;
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Edit", "WindowAdmin", new { id });
    }
}