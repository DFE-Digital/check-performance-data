using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class EndDateController(ILogger<EndDateController> logger, IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/EndDate.cshtml";

    [ActionName("New")]
    [HttpGet("admin/windows/end-date")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }
        
        WindowDateEditItem model = new WindowDateEditItem()
        {
            WindowId = Guid.Empty,
            DateValue = DateTime.UtcNow.AddMonths(1),
            PostUrl = "/admin/windows/end-date",
        };

        return View(PageView, model);
    }
    
    [ActionName("Edit")]
    [HttpGet("admin/windows/{id:guid}/end-date")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        WindowDateEditItem model = new WindowDateEditItem()
        {
            WindowId = window.Id,
            DateValue = window.StartDate,
            PostUrl = $"/admin/windows/{window.Id}/end-date"
        };

        return View(PageView, model);
    }
    
    [HttpPost("admin/windows/end-date")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(WindowDateEditItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }

        DateValidation(model, null);
        
        if (ModelState.ErrorCount > 0)
        {
            return View(PageView, model);
        }

        draft.EndDate = model.DateValue;
        HttpContext.Session.SetObject("CheckingWindowDraft", draft);

        return RedirectToAction("New", draft.NextController(Url));
    }

    [HttpPost("admin/windows/{id:guid}/end-date")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Guid id, WindowDateEditItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDto window = await windowService.GetByIdAsync(id, cancellationToken);

        DateValidation(model, window);
        
        if (ModelState.ErrorCount > 0)
        {
            return View(PageView, model);
        }
        
        if (id != model.WindowId)
        {
            return BadRequest();
        }

        window.StartDate = model.DateValue;
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", id);
    }

    public void DateValidation(WindowDateEditItem model, CheckingWindowDto? windowDto)
    {
        if (model.DateValue < DateTime.UtcNow.Date)
        {
            ModelState.AddModelError(nameof(model.DateValue), "End date can not occur in the past.");
        }

        if (windowDto != null && model.DateValue < windowDto.StartDate)
        {
            ModelState.AddModelError(nameof(model.DateValue),
                $"End date can not occur before the start date ({windowDto.EndDate:dd MM yyyy}).");
        }
    }
}