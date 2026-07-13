using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public sealed class WindowTypeController(IWindowService windowService) : Controller
{
    private const string PageView = "~/Views/WindowAdmin/WindowType.cshtml";

    [HttpGet("admin/windows/window-type")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        WindowTypeItem model = new WindowTypeItem()
        {
            Types = Enum.GetValues<CheckingWindowType>(),
            PostUrl = Url.Action("Submit", "WindowType"),
            CancelUrl =  Url.Action("Index", "CancelCreation"),
            WindowType = draft.CheckingWindowType
        };
        
        return View(PageView, model);
    }
    
    [HttpGet("admin/windows/{id:guid}/window-type")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        WindowTypeItem model = new WindowTypeItem()
        {
            WindowId = id,
            Types = Enum.GetValues<CheckingWindowType>(),
            PostUrl = Url.Action("Update", "WindowType"),
            WindowType = window.CheckingWindowType
        };
        
        return View(PageView, model);
    }
    
    [HttpPost("admin/windows/window-type")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(WindowTypeItem model, CancellationToken cancellationToken)
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

        return Redirect( draft.NextController(Url));
    }
    
    [HttpPost("admin/windows/{id:guid}/window-type")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, WindowTypeItem model, CancellationToken cancellationToken)
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