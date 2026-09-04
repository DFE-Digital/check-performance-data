using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// The only place in the solution that compares a checking exercise's dates against the clock.
/// Fails closed: an exercise that is absent, or a window with no exercises at all, is closed.
/// Closed means "no actions", never "no content" — read-only content stays available for the whole
/// outer window, so callers must not use an empty <see cref="OpenCheckingExercises"/> to hide a
/// card or a page. See docs/16-19-window-model.md.
/// </summary>
/// <remarks>
/// The list is the parameter rather than a window DTO because two unrelated classes are named
/// CheckingWindowDto (LandingPage and WindowManagement) and the second carries its own IsOpen.
/// Taking the list lets either of them call in without the name being ambiguous at the call site.
/// </remarks>
public interface ICheckingExerciseService
{
    /// <summary>True when the exercise exists on the window and brackets now.</summary>
    bool IsOpen(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise);

    /// <summary>Every exercise open right now, in SortOrder. Empty is a valid answer.</summary>
    IReadOnlyList<CheckingExerciseType> OpenCheckingExercises(
        IReadOnlyList<CheckingExerciseDto> exercises);

    /// <summary>The exercise's end date, or null when there is no row for that type.</summary>
    DateTime? EndDateFor(
        IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise);

    /// <summary>The exercise's start date, or null when there is no row for that type (AB#298317).</summary>
    DateTime? StartDateFor(
        IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise);

    /// <summary>
    /// The exercise's row id, or null when there is no row for that type. This is what a
    /// ChangeRequest is stamped with so it can say which exercise it belongs to; the type itself
    /// comes from <see cref="WhatToChangeCheckingExerciseMap"/>, never from a second hardcoding.
    /// </summary>
    /// <remarks>
    /// Deliberately date-blind, unlike <see cref="IsOpen"/>: a draft saved on the last day and read
    /// back the next must still carry its exercise, so the id is returned for a closed exercise
    /// exactly as <see cref="EndDateFor"/> returns a lapsed end date.
    /// </remarks>
    Guid? IdFor(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise);
}

/// <inheritdoc />
/// <remarks>
/// Time comes from the injected <see cref="TimeProvider"/> and is never accepted from a caller —
/// keeping the clock inside is what stops one caller supplying its own and disagreeing with the
/// rest. LandingPageService already reads the clock the same way (GetLocalNow), and the exercise
/// dates are stored as local wall-clock values, so the two comparisons stay in step.
/// </remarks>
public sealed class CheckingExerciseService(TimeProvider timeProvider) : ICheckingExerciseService
{
    public bool IsOpen(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise)
    {
        var now = Now();
        return exercises.Any(e => e.ExerciseType == exercise && Brackets(e, now));
    }

    public IReadOnlyList<CheckingExerciseType> OpenCheckingExercises(
        IReadOnlyList<CheckingExerciseDto> exercises)
    {
        var now = Now();
        return exercises
            .Where(e => Brackets(e, now))
            .OrderBy(e => e.SortOrder)
            .Select(e => e.ExerciseType)
            .ToList();
    }

    public DateTime? EndDateFor(
        IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise) =>
        exercises.FirstOrDefault(e => e.ExerciseType == exercise)?.EndDate;

    public DateTime? StartDateFor(
        IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise) =>
        exercises.FirstOrDefault(e => e.ExerciseType == exercise)?.StartDate;

    public Guid? IdFor(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise) =>
        exercises.FirstOrDefault(e => e.ExerciseType == exercise)?.Id;

    private DateTime Now() => timeProvider.GetLocalNow().DateTime;

    // Inclusive at both ends, matching how the outer window's own dates are compared.
    private static bool Brackets(CheckingExerciseDto exercise, DateTime now) =>
        exercise.StartDate <= now && exercise.EndDate >= now;
}
