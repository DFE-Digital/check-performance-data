using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// The only place in the solution that compares a checking exercise's dates against the clock.
/// Fails closed: an exercise that is absent, or a window with no exercises at all, is closed.
/// Closed means "no actions", never "no content" — read-only content stays available for the whole
/// outer window, so callers must not use an empty
/// <see cref="OpenCheckingExercises(IReadOnlyList{CheckingExerciseDto}, string?)"/> to hide a card or a
/// page. See docs/16-19-window-model.md.
/// </summary>
/// <remarks>
/// The list is the parameter rather than a window DTO because two unrelated classes are named
/// CheckingWindowDto (LandingPage and WindowManagement) and the second carries its own IsOpen.
/// Taking the list lets either of them call in without the name being ambiguous at the call site.
/// </remarks>
public interface ICheckingExerciseService
{
    /// <summary>
    /// True when the exercise exists on the window, brackets now, and is one a school may act on.
    /// A display-only row is never open however its dates read (#466): it is there to be looked at.
    /// Nor is a row schools cannot see (disabled, or outside its visibility dates): a school must
    /// not act on an exercise it cannot see. For one school (AB#307773) the school's extension end
    /// date applies when an extension names it, so a named school stays open after the exercise's
    /// own end. A null or empty LAESTAB is the exercise's own dates.
    /// </summary>
    bool IsOpen(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise, string? laestab);

    /// <summary>
    /// True when the exercise exists on the window and its end date has passed (AB#298317 review).
    /// Not the same as <c>!IsOpen</c>: an exercise that has not started yet is not open, but it
    /// has not closed either, and a "has closed" notice about it would be wrong. An absent
    /// exercise has nothing to have closed, so it answers false. "Has closed" is strictly before:
    /// the last instant is still open. For one school (AB#307773) the school's extension end date
    /// applies when an extension names it. A null or empty LAESTAB is the exercise's own dates.
    /// </summary>
    bool HasClosed(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise, string? laestab);

    /// <summary>
    /// Whether one row is running: it brackets now and is not display-only. Admin pages ask this to
    /// offer Close, so it ignores whether schools can see the row. An admin may disable an exercise,
    /// or move its visibility, and still has to close it and hand its requests over.
    /// </summary>
    bool IsOpen(CheckingExerciseDto exercise);

    /// <summary>
    /// <see cref="HasClosed(IReadOnlyList{CheckingExerciseDto}, CheckingExerciseType, string?)"/> for one
    /// row, for the same reason as <see cref="IsOpen(CheckingExerciseDto)"/>.
    /// </summary>
    bool HasClosed(CheckingExerciseDto exercise);

    /// <summary>
    /// The extension's <em>own</em> end date is still ahead (AB#307773): open to its last instant,
    /// as an exercise is, with no start of its own. This gates Edit and Close early only. It is
    /// not the school's deadline — a school named in an extension closed early keeps the
    /// exercise's own end — so the sweeps, the hand-over and Send requests ask
    /// <see cref="IsOpen(CheckingExerciseDto, CheckingExerciseExtensionDto)"/> instead.
    /// </summary>
    bool IsOpen(CheckingExerciseExtensionDto extension);

    /// <summary>
    /// The extension's <em>own</em> end date has passed (AB#307773): it is read only. Not a licence
    /// to send its requests; see
    /// <see cref="HasClosed(CheckingExerciseDto, CheckingExerciseExtensionDto)"/>.
    /// </summary>
    bool HasClosed(CheckingExerciseExtensionDto extension);

    /// <summary>
    /// The extension's schools are still inside their time (AB#307773): the <em>effective</em> end,
    /// <c>max(exercise.EndDate, extension.EndDate)</c>, is still ahead. An extension closed early
    /// before its exercise ends, or overtaken by a later exercise end, stays open by this answer
    /// until the exercise ends, so nothing sweeps a school still inside the exercise's own dates.
    /// </summary>
    bool IsOpen(CheckingExerciseDto exercise, CheckingExerciseExtensionDto extension);

    /// <summary>
    /// The effective end (see <see cref="IsOpen(CheckingExerciseDto, CheckingExerciseExtensionDto)"/>)
    /// has passed: the extension's requests may be sent, by hand or by the automatic hand-over.
    /// </summary>
    bool HasClosed(CheckingExerciseDto exercise, CheckingExerciseExtensionDto extension);

    /// <summary>
    /// Every exercise a school may act on right now, in tab order. Empty is a valid answer.
    /// Display-only rows are left out for the same reason as in
    /// <see cref="IsOpen(IReadOnlyList{CheckingExerciseDto}, CheckingExerciseType, string?)"/>.
    /// For one school (AB#307773) an exercise stays in the list until the school's extension end
    /// date when an extension names it. A null or empty LAESTAB is the exercises' own dates.
    /// </summary>
    IReadOnlyList<CheckingExerciseType> OpenCheckingExercises(
        IReadOnlyList<CheckingExerciseDto> exercises, string? laestab);

    /// <summary>
    /// The exercise's end date, or null when there is no row for that type. The row is chosen as
    /// in <see cref="IdFor"/>. For one school (AB#307773) it is the school's extension end date
    /// when an extension names it, never earlier than the exercise's own end. A null or empty
    /// LAESTAB is the exercise's own end.
    /// </summary>
    DateTime? EndDateFor(
        IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise, string? laestab);

    /// <summary>
    /// The exercise's start date, or null when there is no row for that type (AB#298317). The row
    /// is chosen as in <see cref="IdFor"/>.
    /// </summary>
    DateTime? StartDateFor(
        IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise);

    /// <summary>
    /// The exercise's row id, or null when there is no row for that type. This is what a
    /// ChangeRequest is stamped with so it can say which exercise it belongs to; the type itself
    /// comes from <see cref="WhatToChangeCheckingExerciseMap"/>, never from a second hardcoding.
    /// </summary>
    /// <remarks>
    /// Deliberately blind to the exercise's dates, unlike
    /// <see cref="IsOpen(IReadOnlyList{CheckingExerciseDto}, CheckingExerciseType, string?)"/>: a draft saved
    /// on the last day and read back the next must still carry its exercise, so the id is returned
    /// for a closed exercise exactly as
    /// <see cref="EndDateFor(IReadOnlyList{CheckingExerciseDto}, CheckingExerciseType, string?)"/> returns a
    /// lapsed end date. Since #466 a window may hold several rows of one type, so the one row that
    /// schools see now and may act on wins (the CheckingExerciseStorageResolver rule); failing
    /// that, the first of the type in tab order (AB#307773).
    /// </remarks>
    Guid? IdFor(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise);

    /// <summary>
    /// Schools see the window at all. It must have started (its first exercise's start date), and
    /// either an exercise's dates still run or an exercise schools can see now has a
    /// <see cref="CheckingExerciseDto.VisibleUntil"/>. That is what keeps a window shown, read
    /// only, after its last exercise closes: KS4 June has one exercise, so without it the window
    /// would vanish at the moment schools lose their actions. With no VisibleUntil set, the window
    /// ends with its last exercise's end date.
    /// </summary>
    /// <remarks>
    /// For one school (AB#307773) each exercise ends on the school's extension end date when an
    /// extension names it, so a school no extension names sees the window end with the exercises'
    /// own dates. A null, empty or blank LAESTAB is no school: the latest end any school sees,
    /// extensions included, which is the window's stored end, so a school with no LAESTAB claim is
    /// not shown the window by one path and hidden by the other.
    /// </remarks>
    bool IsWindowShown(IReadOnlyList<CheckingExerciseDto> exercises, string? laestab);

    /// <summary>
    /// What schools can see and do with <paramref name="exercise"/> now, for the admin summary.
    /// <paramref name="exercises"/> is the whole window:
    /// <see cref="IsWindowShown(IReadOnlyList{CheckingExerciseDto}, string?)"/> decides
    /// whether schools see the window at all. A display-only exercise has no journey, so it is
    /// never <see cref="ExerciseSchoolStatus.VisibleClosed"/>.
    /// </summary>
    ExerciseSchoolStatus StatusOf(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseDto exercise);
}

/// <inheritdoc />
/// <remarks>
/// Time comes from the injected <see cref="TimeProvider"/> and is never accepted from a caller —
/// keeping the clock inside is what stops one caller supplying its own and disagreeing with the
/// rest. LandingPageService already reads the clock the same way (GetLocalNow), and the exercise
/// dates are stored as UK wall-clock values, so the two comparisons stay in step. "Local" is UK
/// time because the hosts register
/// <see cref="DfE.CheckPerformanceData.Application.Common.UkTimeProvider"/> (#535).
/// </remarks>
public sealed class CheckingExerciseService(TimeProvider timeProvider) : ICheckingExerciseService
{
    public bool IsOpen(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise, string? laestab)
    {
        var now = Now();
        return exercises.Any(e => e.ExerciseType == exercise && Actionable(e, now, laestab));
    }

    public bool HasClosed(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise, string? laestab)
    {
        var now = Now();
        // Strictly before: Brackets() keeps the last instant open, so this must not also call it closed.
        return exercises.Any(e => e.ExerciseType == exercise && e.EndDateFor(laestab) < now);
    }

    public bool IsOpen(CheckingExerciseDto exercise) => Running(exercise, Now(), laestab: null);

    // Strictly before, as in the exercise-list overload above.
    public bool HasClosed(CheckingExerciseDto exercise) => exercise.EndDate < Now();

    // AB#307773: an extension has no start; it is open to its last instant, as an exercise is.
    public bool IsOpen(CheckingExerciseExtensionDto extension) => extension.EndDate >= Now();

    public bool HasClosed(CheckingExerciseExtensionDto extension) => extension.EndDate < Now();

    public bool IsOpen(CheckingExerciseDto exercise, CheckingExerciseExtensionDto extension) =>
        exercise.EffectiveEndDateOf(extension) >= Now();

    public bool HasClosed(CheckingExerciseDto exercise, CheckingExerciseExtensionDto extension) =>
        exercise.EffectiveEndDateOf(extension) < Now();

    public IReadOnlyList<CheckingExerciseType> OpenCheckingExercises(
        IReadOnlyList<CheckingExerciseDto> exercises, string? laestab)
    {
        var now = Now();
        // Two separate reasons a row contributes nothing here. A typeless row has no member to
        // contribute at all, since this list is typed by CheckingExerciseType. A row that does
        // have a kind but is display-only is dropped by Actionable: it is shown, never acted on.
        return exercises
            .Where(e => Actionable(e, now, laestab))
            .InTabOrder()
            .Select(e => e.ExerciseType)
            .OfType<CheckingExerciseType>()
            .ToList();
    }

    public DateTime? EndDateFor(
        IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise, string? laestab) =>
        RowFor(exercises, exercise)?.EndDateFor(laestab);

    public DateTime? StartDateFor(
        IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise) =>
        RowFor(exercises, exercise)?.StartDate;

    public Guid? IdFor(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise) =>
        RowFor(exercises, exercise)?.Id;

    public ExerciseSchoolStatus StatusOf(
        IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseDto exercise)
    {
        var now = Now();
        // No school (AB#307773): the window is shown while any school's time runs, but the tag
        // reads the exercise's own dates — the admin sees each extension on its own row.
        if (!WindowShown(exercises, now, laestab: null) || !exercise.IsLiveAt(now)) return ExerciseSchoolStatus.Hidden;

        return exercise.ExerciseType is null || exercise.DisplayOnly || Brackets(exercise, now, laestab: null)
            ? ExerciseSchoolStatus.Visible
            : ExerciseSchoolStatus.VisibleClosed;
    }

    public bool IsWindowShown(IReadOnlyList<CheckingExerciseDto> exercises, string? laestab) =>
        WindowShown(exercises, Now(), laestab);

    private DateTime Now() => timeProvider.GetLocalNow().DateTime;

    // Since #466 a window may hold several releases of one kind, and the journeys read the live
    // interactive one (CheckingExerciseStorageResolver). The id a request is stamped with, and
    // the end date a school is shown, must name that same row — but unlike the resolver this
    // falls back rather than failing closed, because a draft read back after its exercise was
    // disabled must still carry its exercise (see the IdFor remark on the interface).
    private CheckingExerciseDto? RowFor(IReadOnlyList<CheckingExerciseDto> exercises, CheckingExerciseType exercise)
    {
        var now = Now();
        var ofKind = exercises.Where(e => e.ExerciseType == exercise).InTabOrder().ToList();
        var live = ofKind.Where(e => e.IsLiveAt(now)).ToList();
        var interactive = live.Where(e => !e.DisplayOnly).ToList();
        if (interactive.Count > 0) live = interactive;
        return live.Count == 1 ? live[0] : ofKind.FirstOrDefault();
    }

    // The end date is inclusive and VisibleUntil exclusive, each as it is everywhere else. With no
    // school the end is the latest any school sees, so an extension keeps the window shown for the
    // schools it names; with a school it is that school's (AB#307773). A blank LAESTAB is no school,
    // not a school that no extension names.
    private static bool WindowShown(IReadOnlyList<CheckingExerciseDto> exercises, DateTime now, string? laestab) =>
        exercises.Count > 0
        && exercises.Min(e => e.StartDate) <= now
        && (exercises.Max(e => string.IsNullOrWhiteSpace(laestab) ? e.LatestEndDate : e.EndDateFor(laestab)) >= now
            || exercises.Any(e => e.VisibleUntil is not null && e.IsLiveAt(now)));

    // "Open" is two questions, not one: is it running, and is it a thing a school may act on.
    // Keeping them apart is what lets an admin mark an exercise read-only — a pupil-data exercise
    // reissued with refreshed data after its checking window shut — without having to falsify its
    // dates to make the actions go away (#466). A school may act only on an exercise it can see
    // (Actionable). The admin's per-row question (Running) leaves that out, so a disabled
    // exercise can still be closed and its requests sent.
    private static bool Running(CheckingExerciseDto exercise, DateTime now, string? laestab) =>
        !exercise.DisplayOnly && Brackets(exercise, now, laestab);

    private static bool Actionable(CheckingExerciseDto exercise, DateTime now, string? laestab) =>
        Running(exercise, now, laestab) && exercise.IsLiveAt(now);

    // Inclusive at both ends, matching how the outer window's own dates are compared. Dates only:
    // HasClosed reports a lapsed end date, which is a fact about the dates whatever the row is.
    // A named school's end is its extension's, never earlier than the exercise's own (AB#307773).
    private static bool Brackets(CheckingExerciseDto exercise, DateTime now, string? laestab) =>
        exercise.StartDate <= now && exercise.EndDateFor(laestab) >= now;
}
