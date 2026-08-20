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
            // #319: one section per checking exercise, each with its own dates, files and
            // validation state. There is no window-level validate button any more — an exercise
            // validates on its own, and a window is usable while another is still unvalidated.
            Exercises = w.Exercises
                .OrderBy(e => e.SortOrder)
                .Select(e => new ExerciseSummarySection
                {
                    WindowId = w.Id,
                    ExerciseType = e.ExerciseType,
                    Label = ExerciseLabels.For(e.ExerciseType),
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    IsValidated = e.IsValidated,
                    ValidatedAt = e.ValidatedAt,
                    IsStale = e.ValidatedAt is not null && !e.IsValidated,
                    Datasets = e.Datasets
                        .OrderBy(d => d.SortOrder)
                        .Select(d => new DatasetSummaryRow
                        {
                            WindowId = w.Id,
                            Exercise = e.ExerciseType,
                            Name = d.Name,
                            Label = DatasetLabels.For(d.Name),
                            IngressFile = d.IngressFile,
                            SchemaFile = d.SchemaFile
                        })
                        .ToList()
                })
                .ToList()
        };
        return View("~/Views/WindowAdmin/Summary.cshtml", vm);
    }
}