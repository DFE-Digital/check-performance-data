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
            Hour = window.StartDate.Hour,
            Minute = window.StartDate.Minute,
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
            // New windows default to opening at midnight; the admin can change it.
            Hour = draft.StartDate?.Hour ?? DefaultStartHour,
            Minute = draft.StartDate?.Minute ?? 0,
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

        if (ModelState.IsValid)
        {
            DateValidation(model.DateTimeValue, null);
        }

        if (!ModelState.IsValid)
        {
            return View(PageView, model);
        }

        draft.StartDate = model.DateTimeValue;
        HttpContext.Session.SetObject("CheckingWindowDraft", draft);

        return Redirect(draft.NextController(Url));
    }

    [HttpPost("admin/windows/{id:guid}/start-date")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, WindowDateEditItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDto window = await windowService.GetByIdAsync(id, cancellationToken);

        if (ModelState.IsValid)
        {
            DateValidation(model.DateTimeValue, window);
        }

        if (!ModelState.IsValid)
        {
            return View(PageView, model);
        }

        if (id != model.WindowId)
        {
            return BadRequest();
        }

        window.StartDate = model.DateTimeValue!.Value;
        await windowService.UpdateAsync(window, cancellationToken);

        return RedirectToAction("Index", "Summary", new { id = id });
    }

    public void DateValidation(DateTime? value, CheckingWindowDto? windowDto)
    {
        if (value < DateTime.UtcNow.Date)
        {
            ModelState.AddModelError(nameof(WindowDateEditItem.DateValue), "Start date can not occur in the past.");
        }

        if (windowDto != null && value > windowDto.EndDate)
        {
            ModelState.AddModelError(nameof(WindowDateEditItem.DateValue), $"Start date can not occur after the end date ({windowDto.EndDate:dd MM yyyy HH:mm}).");
        }
    }

    private const int DefaultStartHour = 0;
}