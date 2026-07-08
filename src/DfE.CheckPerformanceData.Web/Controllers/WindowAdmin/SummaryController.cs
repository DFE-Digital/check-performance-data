using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public class SummaryController(ILogger<SummaryController> logger, IWindowService windowService): Controller
{
   
    [HttpGet("admin/windows/summary/{id}")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto w = await windowService.GetByIdAsync(id, cancellationToken);
        WindowEditItem vm = new WindowEditItem
        {
            Id = w.Id,
            Title = w.Title,
            StartDate = w.StartDate,
            EndDate = w.EndDate,
            KeyStage = w.KeyStage,
            CheckingWindowType = w.CheckingWindowType,
            IngressFile = w.IngressFile,
            SchemaFile = w.SchemaFile
            
        };
        return View("~/Views/WindowAdmin/Summary.cshtml", vm);
    }
}