using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Common;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class SummaryController(IWindowService windowService) : Controller
{

    [HttpGet("admin/windows/summary/{id:guid}")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
    {
        CheckingWindowDto? w = await windowService.GetByIdAsync(id, cancellationToken);
        if (w is null)
        {
            return NotFound();
        }

        WindowEditItem vm = new WindowEditItem
        {
            WindowId = w.Id,
            Title = w.Title,
            TurnaroundCommitment = w.TurnaroundCommitment,
            NextOpportunity = NextOpportunityText.For(w.NextOpportunity),
            StartDate = w.StartDate,
            EndDate = w.EndDate,
            KeyStage = w.KeyStage,
            CheckingWindowType = w.CheckingWindowType,
            // #319: one section per checking exercise, each with its own dates, files and
            // validation state. There is no window-level validate button any more — an exercise
            // validates on its own, and a window is usable while another is still unvalidated.
            Exercises = w.Exercises
                .OrderBy(e => e.SortOrder)
                .ThenBy(e => e.Id)
                .Select(e => new ExerciseSummarySection
                {
                    WindowId = w.Id,
                    ExerciseId = e.Id,
                    ExerciseType = e.ExerciseType,
                    TabName = e.TabName,
                    IsEnabled = e.IsEnabled,
                    Label = e.Name ?? ExerciseLabels.For(e.ExerciseType),
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
                            ExerciseId = e.Id,
                            Exercise = e.ExerciseType,
                            Name = d.Name,
                            Label = DatasetLabels.For(d.Name),
                            IngressFile = d.IngressFile,
                            SchemaFile = d.SchemaFile,
                            Required = d.Required
                        })
                        .ToList()
                })
                .ToList()
        };
        return View("~/Views/WindowAdmin/Summary.cshtml", vm);
    }
}