using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

public sealed class SummaryController(IWindowService windowService): Controller
{
   
    [HttpGet("admin/windows/summary/{id:guid}")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto w = await windowService.GetByIdAsync(id, cancellationToken);
        WindowEditItem vm = new WindowEditItem
        {
            WindowId = w.Id,
            Title = w.Title,
            StartDate = w.StartDate,
            EndDate = w.EndDate,
            KeyStage = w.KeyStage,
            CheckingWindowType = w.CheckingWindowType,
            Datasets = w.AllDatasets
                .Select(d => new DatasetSummaryRow
                {
                    WindowId = w.Id,
                    Name = d.Name,
                    Label = DatasetLabels.For(d.Name),
                    IngressFile = d.IngressFile,
                    SchemaFile = d.SchemaFile
                })
                .ToList(),
            PostUrl = Url.Action("Index", "ValidateWindow", new {id = w.Id})
        };
        return View("~/Views/WindowAdmin/Summary.cshtml", vm);
    }
}