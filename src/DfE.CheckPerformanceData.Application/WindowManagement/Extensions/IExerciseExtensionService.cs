namespace DfE.CheckPerformanceData.Application.WindowManagement.Extensions;

/// <summary>
/// AB#307773: the rules behind adding, editing and closing an extension of a checking exercise —
/// more time for the schools it names. The writes themselves are
/// <see cref="IExerciseExtensionRepository"/>'s; this decides whether they may happen and turns
/// each refusal into a status the admin page can say a sentence about. The caller's permission is
/// the route's concern, not this one's.
/// </summary>
public interface IExerciseExtensionService
{
    /// <summary>
    /// The schools in <paramref name="laestabs"/> with no file in what the exercise shows now — a
    /// warning, not a refusal: the admin may still extend them. With no release yet, every school
    /// is listed and <see cref="ExtensionWarnings.ExerciseHasNoRelease"/> says why. An unknown
    /// window or exercise warns of nothing.
    /// </summary>
    Task<ExtensionWarnings> FindSchoolsWithoutDataAsync(
        Guid windowId, Guid exerciseId, IReadOnlyList<string> laestabs, CancellationToken ct);

    Task<ExtensionSaveResult> AddAsync(ExtensionCommand command, CancellationToken ct);

    /// <summary>
    /// <paramref name="expectedVersion"/> is the extension's version as the admin was shown it: if
    /// it has moved since (another edit, an early close), nothing is written and the answer is
    /// <see cref="ExtensionSaveStatus.Stale"/>.
    /// </summary>
    Task<ExtensionSaveResult> EditAsync(
        Guid extensionId, int expectedVersion, ExtensionCommand command, CancellationToken ct);

    /// <summary>
    /// Ends an open extension now, as <see cref="IExerciseEarlyClosureService"/> ends an exercise:
    /// <see cref="EarlyClosureStatus.NotFound"/>, <see cref="EarlyClosureStatus.NotOpen"/> or
    /// <see cref="EarlyClosureStatus.Closed"/>.
    /// </summary>
    Task<EarlyClosureResult> CloseEarlyAsync(
        Guid windowId, Guid exerciseId, Guid extensionId, ExtensionActor actor, CancellationToken ct);
}

/// <summary>An add or edit of an extension. <see cref="Laestabs"/> may be in either form; it is stored as digits.</summary>
public sealed record ExtensionCommand(
    Guid WindowId, Guid ExerciseId, DateTime EndDate, IReadOnlyList<string> Laestabs, ExtensionActor Actor);

/// <summary>The checks of an add or edit, in the order they are made; the first that fails is the answer.</summary>
public enum ExtensionSaveStatus
{
    /// <summary>Written. <see cref="ExtensionSaveResult.ExtensionId"/> is the extension's id.</summary>
    Saved,

    /// <summary>No such window, no such exercise on it, a data share, or (on edit) no such extension of that exercise.</summary>
    NotFound,

    /// <summary>Edit only: the extension's end date has passed. A closed extension is read only.</summary>
    Closed,

    /// <summary>The end date does not come after the exercise's own end, so it would add no time.
    /// <see cref="ExtensionSaveResult.ExerciseEnd"/> says what it must follow.</summary>
    EndDateNotAfterExercise,

    /// <summary>The end date has already passed, so the extension would be closed as it is saved.</summary>
    EndDateNotInFuture,

    /// <summary>No school named. The form refuses this first; this is the backstop.</summary>
    NoSchools,

    /// <summary>A school is already in another extension of this exercise, and a school is in at
    /// most one. <see cref="ExtensionSaveResult.Conflicts"/> names each.</summary>
    SchoolInAnotherExtension,

    /// <summary>The extension's end date changed since the admin read it, or the write lost a race to
    /// the unique index and a fresh read finds nothing to refuse. Nothing was written.</summary>
    Stale
}

/// <summary>A school already in another extension of the exercise, and when that extension ends — for the error text.</summary>
public sealed record ExtensionConflict(string Laestab, DateTime OtherExtensionEnd);

public sealed record ExtensionSaveResult(
    ExtensionSaveStatus Status,
    Guid? ExtensionId = null,
    DateTime? ExerciseEnd = null,
    IReadOnlyList<ExtensionConflict>? Conflicts = null,
    // Edit only: the schools the edit took out, digits. Empty when none.
    IReadOnlyList<string>? RemovedSchools = null,
    // What their sweep did, when the exercise had already closed by its own end (finding 2); null
    // when the exercise still runs, nobody was removed, or the sweep failed.
    CloseExerciseResult? RemovedSchoolsSwept = null,
    // The edit is saved but the removed schools' sweep threw; the exercise's Send requests sends them.
    bool RemovedSchoolsSweepFailed = false);

/// <summary>What the admin is warned of before confirming an extension. Digits, in the order asked.</summary>
public sealed record ExtensionWarnings(IReadOnlyList<string> LaestabsWithoutData, bool ExerciseHasNoRelease);
