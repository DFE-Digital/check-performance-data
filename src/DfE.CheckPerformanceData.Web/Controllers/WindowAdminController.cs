using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class WindowAdminController(
    IWindowService windowService,
    ICheckingExerciseService checkingExercises,
    IAdminAccessPolicy accessPolicy) : Controller
{
    [HttpGet("admin/windows")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        PageResult? pageResult = await windowService.GetAllDataAsync(cancellationToken);
        List<WindowListItem> windows = pageResult?.Windows.Select(window => new WindowListItem
        {
            Id = window.Id,
            Name = window.Title,
            StartDate = window.StartDate,
            Exercises = window.Exercises.InTabOrder().Select(exercise => new CheckingExerciseListItem
            {
                Name = exercise.Name ?? ExerciseLabels.For(exercise.ExerciseType),
                KindLabel = ExerciseLabels.For(exercise.ExerciseType),
                Status = checkingExercises.StatusOf(window.Exercises, exercise),
                DataStatus = exercise.DataStatus
            }).ToList()
        }).ToList() ?? [];

        // The wizard's create steps are gated on NewWindow, not ManageWindow, so the button that
        // starts it is only shown to a user who can finish it.
        return View(new WindowViewModel(windows)
        {
            CanCreateWindow = await accessPolicy.CanAccessAsync(User, AdminNavKeys.NewWindow)
        });
    }
}
