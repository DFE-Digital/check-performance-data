using System.Text.Json;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Session;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

[Authorize]
[Route("check-data")]
public sealed class CheckingDataController(
    ICheckingDataCatalogue catalogue, ICheckingDataReader reader,
    ICurrentUserService user, TimeProvider clock) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.OrganisationLaestab)) return Forbid();
        var tabs = new List<CheckingDataTab>();
        var visible = await catalogue.GetVisibleAsync(cancellationToken);
        foreach (var exercise in visible)
        {
            var bytes = await reader.ReadAsync(exercise, user.OrganisationLaestab, cancellationToken);
            if (bytes is null) continue; // Only the signed-in school's datasets are shown.
            using var json = JsonDocument.Parse(bytes);
            var rows = json.RootElement.EnumerateArray().Select(row => row.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.ToString())).ToList();
            tabs.Add(new(exercise, CanStart(exercise, visible), rows));
        }
        return View(tabs);
    }

    [HttpGet("{exerciseId:guid}/download")]
    public async Task<IActionResult> Download(Guid exerciseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.OrganisationLaestab)) return Forbid();
        var exercise = (await catalogue.GetVisibleAsync(cancellationToken)).SingleOrDefault(e => e.Id == exerciseId);
        if (exercise is null) return NotFound();
        var bytes = await reader.ReadAsync(exercise, user.OrganisationLaestab, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/json", $"{exercise.Id}_{exercise.DataType}.json");
    }

    [HttpPost("{exerciseId:guid}/start")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid exerciseId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.OrganisationLaestab)) return Forbid();
        var visible = await catalogue.GetVisibleAsync(cancellationToken);
        var exercise = visible.SingleOrDefault(e => e.Id == exerciseId);
        if (exercise is null) return NotFound();
        if (!CanStart(exercise, visible)) return StatusCode(StatusCodes.Status403Forbidden);
        if (await reader.ReadAsync(exercise, user.OrganisationLaestab, cancellationToken) is null) return NotFound();
        // Match the legacy data-page entry: a new request cannot inherit a previous draft.
        HttpContext.Session.SetString("SelectedWindowId", exercise.WindowId.ToString());
        HttpContext.Session.SetRequestState(exercise.WindowId, new RequestState
        {
            SelectedNextStep = exercise.ExerciseType == CheckingExerciseType.PupilData
                ? NextSteps.RequestChange : NextSteps.ResultsEnquiry
        });
        HttpContext.Session.ClearBulkEditMode(exercise.WindowId);
        HttpContext.Session.ClearSingleEditMode(exercise.WindowId);
        return exercise.ExerciseType switch
        {
            CheckingExerciseType.PupilData => RedirectToAction("Index", "WhatToChange", new { windowId = exercise.WindowId }),
            CheckingExerciseType.ResultsEnquiry => RedirectToAction("Index", "ResultIssue", new { windowId = exercise.WindowId }),
            _ => StatusCode(StatusCodes.Status403Forbidden)
        };
    }
    private bool CanStart(CheckingDataExercise exercise, IReadOnlyList<CheckingDataExercise> visible)
        => exercise.CanAct(clock.GetLocalNow().DateTime)
            && exercise.DataType == CheckingExerciseBlobPaths.DefaultDataType(exercise.ExerciseType)
            && visible.Count(e => e.WindowId == exercise.WindowId && e.ExerciseType == exercise.ExerciseType
                && e.DataType == exercise.DataType) == 1;

}

public sealed record CheckingDataTab(CheckingDataExercise Exercise, bool CanAct,
    IReadOnlyList<Dictionary<string, string>> Rows)
{
    public IReadOnlyList<string> Columns => Rows.SelectMany(r => r.Keys).Distinct().ToList();
}
