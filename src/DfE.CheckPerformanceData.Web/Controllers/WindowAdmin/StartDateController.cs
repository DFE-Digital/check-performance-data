using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public sealed class StartDateController(IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/StartDate.cshtml";

    [HttpGet("admin/windows/{id:guid}/start-date")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
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
            PostUrl = Url.Action("Update", "StartDate", new { id = window.Id}),
            CancelUrl = Url.Action("Index", "Summary", new { id = window.Id})
        };

        return View(PageView, model);
    }
    
    [HttpGet("admin/windows/start-date")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        CheckingWindowDraft? draft = HttpContext.Session.GetObject<CheckingWindowDraft>("CheckingWindowDraft");

        if (draft == null)
        {
            return BadRequest("No draft data");
        }
        
        WindowDateEditItem model = new WindowDateEditItem()
        {
            WindowId = Guid.Empty,
            DateValue = draft.StartDate,
            PostUrl =  Url.Action("Submit", "StartDate"),
            CancelUrl =  Url.Action("Index", "CancelCreation")
            
        };

        return View(PageView, model);
    }
    
    [HttpPost("admin/windows/start-date")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(WindowDateEditItem model, CancellationToken cancellationToken)
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

        return Redirect(draft.NextController(Url));
    }

    [HttpPost("admin/windows/{id:guid}/start-date")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, WindowDateEditItem model, CancellationToken cancellationToken)
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

        window.StartDate = model.DateValue!.Value;
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { id = id });
    }

    public void DateValidation(WindowDateEditItem model, CheckingWindowDto? windowDto)
    {
        if (model.DateValue < DateTime.UtcNow.Date)
        {
            ModelState.AddModelError(nameof(model.DateValue), "Start date can not occur in the past.");
        }

        if (windowDto != null && model.DateValue > windowDto.EndDate)
        {
            ModelState.AddModelError(nameof(model.DateValue), $"Start date can not occur after the end date ({windowDto.EndDate:dd MM yyyy}).");
        }
    }
}