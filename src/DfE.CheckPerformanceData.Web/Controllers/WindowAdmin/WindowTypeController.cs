using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class WindowTypeController(ILogger<WindowTypeController> logger, IWindowService windowService) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/WindowType.cshtml";

    [ActionName("New")]
    [HttpGet("admin/windows/window-type")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        WindowTypeItem model = new WindowTypeItem()
        {
            WindowId = Guid.NewGuid(),
            Types = Enum.GetValues<CheckingWindowType>(),
            PostUrl = "/admin/windows/window-type",
            WindowType = draft.CheckingWindowType
        };
        
        return View(PageView, model);
    }
    
    [ActionName("Edit")]
    [HttpGet("admin/windows/{id:guid}/window-type")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        WindowTypeItem model = new WindowTypeItem()
        {
            WindowId = Guid.NewGuid(),
            Types = Enum.GetValues<CheckingWindowType>(),
            PostUrl = "/admin/windows/window-type",
            WindowType = window.CheckingWindowType
        };
        
        return View(PageView, model);
    }
    
    [HttpPost("admin/windows/window-type")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(WindowTypeItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }
       
        if (ModelState.ErrorCount > 0)
        {
            return View(PageView, model);
        }

        draft.CheckingWindowType = model.WindowType;
        HttpContext.Session.SetObject("CheckingWindowDraft", draft);

        return RedirectToAction("New", draft.NextController(Url));
    }
    
    [HttpPost("admin/windows/{id:guid}/window-type")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Guid id, WindowTypeItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDto window = await windowService.GetByIdAsync(id, cancellationToken);

        if (model.WindowType == null)
        {
            ModelState.AddModelError("WindowType", "Please select a window type");
        }
        
        if (ModelState.ErrorCount > 0)
        {
            return View(PageView, model);
        }
        
        if (id != model.WindowId)
        {
            return BadRequest();
        }

        window.CheckingWindowType = model.WindowType.Value;
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", id);
    }
}