using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class StartDateController(ILogger<StartDateController> logger, IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/StartDate.cshtml";

    [ActionName("Edit")]
    [HttpGet("admin/windows/{id:guid}/start-date")]
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
            PostUrl = $"/admin/windows/{window.Id}/start-date"
        };

        return View(PageView, model);
    }
    
    [ActionName("New")]
    [HttpGet("admin/windows/start-date")]
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
            PostUrl =  $"/admin/windows/start-date"
        };

        return View(PageView, model);
    }
    
    [HttpPost("admin/windows/start-date")]
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

        draft.StartDate = model.DateValue;
        HttpContext.Session.SetObject("CheckingWindowDraft", draft);

        return RedirectToAction("New", draft.NextController());
    }

    [HttpPost("admin/windows/{id:guid}/start-date")]
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
        if (model.DateValue < DateTime.UtcNow)
        {
            ModelState.AddModelError(nameof(model.DateValue), "Start date can not occur in the past.");
        }

        if (windowDto != null && model.DateValue > windowDto.EndDate)
        {
            ModelState.AddModelError(nameof(model.DateValue), $"Start date can not occur after the end date ({windowDto.EndDate:dd MM yyyy}).");
        }
    }
}