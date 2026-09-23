using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[RequireAdminSection(AdminNavKeys.ManageWindow)]
public sealed class WindowAdminController(
    IWindowService windowService,
    IQuestionFlowConfigSource questionFlows,
    TimeProvider timeProvider) : Controller
{
    [HttpGet("admin/windows")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        PageResult? pageResult = await windowService.GetAllDataAsync(cancellationToken);
        var now = timeProvider.GetLocalNow().DateTime;
        List<WindowListItem> windows = pageResult?.Windows.Select(window => new WindowListItem
        {
            Id = window.Id,
            Name = window.Title,
            IsOpen = window.IsOpen,
            IsPublished = true,
            // #466: a display-only exercise renders too, alongside pupil data and results enquiry.
            Exercises = window.Exercises.OrderBy(e => e.SortOrder).Select(exercise =>
            {
                // Lifted equality: a null ExerciseType matches no WhatToChange journey, so a
                // display-only exercise always reports no missing journeys, never a false positive.
                var missing = Enum.GetValues<WhatToChange>()
                    .Where(journey => WhatToChangeCheckingExerciseMap.CheckingExerciseFor(journey) == exercise.ExerciseType)
                    .Where(journey => !questionFlows.Exists(journey, window.CheckingWindowType))
                    .Select(JourneyLabel)
                    .ToList();
                var status = missing.Count > 0 ? "Missing journeys"
                    : now < exercise.StartDate ? "Upcoming"
                    : now > exercise.EndDate ? "Closed" : "Open";
                return new CheckingExerciseListItem
                {
                    Name = exercise.Name,
                    Status = status,
                    MissingJourneys = missing
                };
            }).ToList()
        }).ToList() ?? [];

        return View(new WindowViewModel(windows));
    }

    private static string JourneyLabel(WhatToChange journey) => journey switch
    {
        WhatToChange.IncorrectGrade => "Incorrect grade",
        WhatToChange.MissingQualification => "Missing qualification",
        WhatToChange.ResultDoesNotBelong => "Result does not belong",
        _ => journey.ToString()
    };
}
