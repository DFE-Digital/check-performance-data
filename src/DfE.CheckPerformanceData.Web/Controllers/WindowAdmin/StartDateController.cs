using System.Runtime.InteropServices.JavaScript;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class StartDateController(ILogger<TitleController> logger, IWindowService windowService): Controller
{
    private const string PageView = "~/Views/WindowAdmin/StartDate.cshtml";


    [HttpGet("admin/windows/{id:guid}/start-date")]
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

    [HttpPost("admin/windows/{id:guid}/start-date")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Guid id, WindowStartDateEditItem model, CancellationToken cancellationToken)
    {
        CheckingWindowDto window = await windowService.GetByIdAsync(id, cancellationToken);

        DateValidation(model, window, cancellationToken);
        
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

    public void DateValidation(WindowStartDateEditItem model, CheckingWindowDto? windowDto, CancellationToken cancellationToken)
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