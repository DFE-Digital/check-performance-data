using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class StartDateController(ILogger<TitleController> logger, IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/StartDate.cshtml";


    [ActionName("EditWindow")]
    [HttpGet("admin/windows/{id:guid}/start-date", Name =  "EditWindow")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        var window = await windowService.GetByIdAsync(id, cancellationToken);

        if (window is null)
        {
            return NotFound();
        }

        WindowStartDateEditItem model = new WindowStartDateEditItem()
        {
            WindowId = window.Id,
            StartDate = window.StartDate
        };

        return View(PageView, model);
    }
    
    [ActionName("NewWindow")]
    [HttpGet("admin/windows/start-date")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }
        
        WindowStartDateEditItem model = new WindowStartDateEditItem()
        {
            WindowId = Guid.Empty,
            StartDate = DateTime.UtcNow.AddMonths(1)
        };

        return View(PageView, model);
    }
    
    [HttpPost("admin/windows/start-date")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(WindowStartDateEditItem model, CancellationToken cancellationToken)
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

        draft.StartDate = model.StartDate;
        HttpContext.Session.SetObject("CheckingWindowDraft", draft);

        throw new NotImplementedException("pass to end date controller");
        return RedirectToAction();
    }

    [HttpPost("admin/windows/{id:guid}/start-date")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Guid id, WindowStartDateEditItem model, CancellationToken cancellationToken)
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

        window.StartDate = model.StartDate;
        await windowService.UpdateAsync(window, cancellationToken);

        return Redirect($"/admin/windows/summary/{id}");
    }

    public void DateValidation(WindowStartDateEditItem model, CheckingWindowDto? windowDto)
    {
        if (model.StartDate < DateTime.UtcNow)
        {
            ModelState.AddModelError(nameof(model.StartDate), "Start date can not occur in the past.");
        }

        if (windowDto != null && model.StartDate > windowDto.EndDate)
        {
            ModelState.AddModelError(nameof(model.StartDate), $"Start date can not occur after the end date ({windowDto.EndDate:dd MM yyyy}).");
        }
    }
}