using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// AB#307773: the writes behind an exercise extension. Each one is a single transaction holding
/// its WindowAdmin audit row, and each one re-derives the window's own end from the exercise ends
/// (<c>WindowEndDate</c>); extension ends never reach the window row.
/// Whether an extension may be added, edited or closed is decided by the caller; the
/// guards here only refuse to act on a row that changed since the caller read it.
/// </summary>
public interface IExerciseExtensionRepository
{
    /// <summary>
    /// Writes the extension and its schools; returns the new extension's id.
    /// Throws <see cref="ExtensionSchoolTakenException"/> when the unique index on (exercise, LAESTAB) refuses the write.
    /// </summary>
    Task<Guid> AddAsync(NewExtension extension, CancellationToken ct);

    /// <summary>
    /// A compare-and-set on <see cref="ExtensionEdit.ExpectedVersion"/>: false, and nothing written,
    /// when the stored version differs (a second admin's edit, a second press, an early close) —
    /// every change bumps it, so a school-list-only edit is guarded too. The school rows become
    /// <see cref="ExtensionEdit.Laestabs"/>; rows kept keep their ids.
    /// Throws <see cref="ExtensionSchoolTakenException"/> when the unique index on (exercise, LAESTAB) refuses the write.
    /// </summary>
    Task<bool> UpdateAsync(ExtensionEdit edit, CancellationToken ct);

    /// <summary>
    /// A compare-and-set on <see cref="ExtensionEarlyClosure.ScheduledEnd"/>, as
    /// <see cref="IWindowRepository.CloseExerciseEarlyAsync"/> is for the exercise. The audit row
    /// is the early-closure stamp, as with the exercise; there is no column for it.
    /// </summary>
    Task<bool> CloseEarlyAsync(ExtensionEarlyClosure closure, CancellationToken ct);

    /// <summary>The audit row for an admin's Send requests on a closed extension. The sweep itself is not part of it.</summary>
    Task RecordRequestsSentAsync(ExtensionRequestsSent record, CancellationToken ct);

    /// <summary>
    /// Sets <c>RequestsSentAt</c> when it is still null, whichever sweep runs first;
    /// true when this call set it.
    /// <paramref name="atUtc"/> is stored as the UTC instant, as the add time is.
    /// </summary>
    Task<bool> MarkRequestsSentAsync(Guid extensionId, DateTime atUtc, CancellationToken ct);
}

/// <summary>The admin behind an extension write: the sign-in subject for the audit row's UserId, the name for its payload.</summary>
public sealed record ExtensionActor(string UserId, string DisplayName);

/// <summary>One extension to add (AB#307773).</summary>
public sealed record NewExtension
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }

    /// <summary>The exercise's kind, for the audit row only.</summary>
    public required CheckingExerciseType Exercise { get; init; }

    /// <summary>A UK wall-clock value, like every exercise date.</summary>
    public required DateTime EndDate { get; init; }

    /// <summary>Digits only (9334290). A repeat is written once.</summary>
    public required IReadOnlyList<string> Laestabs { get; init; }

    public required ExtensionActor Actor { get; init; }

    /// <summary>The instant of the add in UTC: the extension's CreatedAt and the audit row's Timestamp.</summary>
    public required DateTime CreatedAtUtc { get; init; }
}

/// <summary>One edit of an open extension (AB#307773).</summary>
public sealed record ExtensionEdit
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required CheckingExerciseType Exercise { get; init; }
    public required Guid ExtensionId { get; init; }

    /// <summary>The version the admin was shown. The write's guard: a different stored version means
    /// somebody else edited or closed the extension meanwhile, and nothing is written.</summary>
    public required int ExpectedVersion { get; init; }

    /// <summary>The end date the admin was shown, for the audit payload only. Not a guard.</summary>
    public required DateTime PreviousEndDate { get; init; }

    public required DateTime NewEndDate { get; init; }

    /// <summary>Every school the extension names after the edit, digits only.</summary>
    public required IReadOnlyList<string> Laestabs { get; init; }

    public required ExtensionActor Actor { get; init; }
    public required DateTime EditedAtUtc { get; init; }
}

/// <summary>One early closure of an extension (AB#307773), the twin of <see cref="ExerciseEarlyClosure"/>.</summary>
public sealed record ExtensionEarlyClosure
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required CheckingExerciseType Exercise { get; init; }
    public required Guid ExtensionId { get; init; }

    /// <summary>The end date the extension had when the close was confirmed. The write's guard.</summary>
    public required DateTime ScheduledEnd { get; init; }

    /// <summary>The extension's new end date: a local wall-clock value.</summary>
    public required DateTime NewEndDate { get; init; }

    public required ExtensionActor Actor { get; init; }
    public required DateTime ClosedAtUtc { get; init; }
}

/// <summary>What an admin's Send requests on a closed extension did (AB#307773), for its audit row.</summary>
public sealed record ExtensionRequestsSent
{
    public required Guid WindowId { get; init; }
    public required Guid ExerciseId { get; init; }
    public required CheckingExerciseType Exercise { get; init; }
    public required Guid ExtensionId { get; init; }

    /// <summary>The extension's end date, so the row says which deadline the send followed.</summary>
    public required DateTime ExtensionEnd { get; init; }

    public required int RequestsSent { get; init; }
    public required int DraftsCancelled { get; init; }
    public required ExtensionActor Actor { get; init; }
    public required DateTime SentAtUtc { get; init; }
}

/// <summary>
/// <see cref="IExerciseExtensionRepository.AddAsync"/> or <see cref="IExerciseExtensionRepository.UpdateAsync"/>
/// was refused by the database's unique index on (exercise, LAESTAB): between the caller's read and
/// this write, another extension of the same exercise took one of the schools. Nothing was written.
/// The caller reads again and refuses as it would have, had it seen that extension.
/// </summary>
public sealed class ExtensionSchoolTakenException(Exception inner)
    : Exception("A school named in the extension is already in another extension of the exercise.", inner);
