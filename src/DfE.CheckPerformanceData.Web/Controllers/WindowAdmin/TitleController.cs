using System.Text.Json;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class TitleController(ILogger<TitleController> logger, IWindowService windowService): Controller
{
    [HttpGet("admin/windows/title")]
    [HttpGet("admin/windows/{id:guid}/title")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        WindowTitleEditItem model = new WindowTitleEditItem();
        
        if (id == Guid.Empty)
        {
            model.Title = "New window";
            return View("~/Views/WindowAdmin/Title.cshtml", model);
        }

        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        model = new WindowTitleEditItem
        {
            WindowId = window.Id,
            Title = window.Title
        };

        return View("~/Views/WindowAdmin/Title.cshtml", model);
    }
    
    [HttpPost("admin/windows/title")]
    [HttpPost("admin/windows/{id:guid}/title")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Guid id, WindowTitleEditItem model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            ModelState.AddModelError(nameof(model.Title), "Enter a title");
            return View("~/Views/WindowAdmin/Title.cshtml", model);
        }
        
        if (id == Guid.Empty)
        {
            CheckingWindowDraft draft = new CheckingWindowDraft
            {
                Title = model.Title
            };
            HttpContext.Session.SetString(
                "CheckingWindowDraft",
                JsonSerializer.Serialize(draft));
            
            throw new NotImplementedException("Will redirect to required controllers"); 
            
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