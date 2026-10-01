using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface IWindowRepository
{
    Task<List<CheckingWindowDto>> GetAllWindowsAsync(CancellationToken cancellationToken);
    Task<CheckingWindowDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken);
    Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken);

    /// <summary>
    /// AB#301022: moves one exercise's end date to <see cref="ExerciseEarlyClosure.NewEndDate"/>,
    /// re-derives the window's own end date, and writes the WindowAdmin / ClosedEarly audit row,
    /// in one transaction. A compare-and-set: nothing is written unless the exercise still holds
    /// <see cref="ExerciseEarlyClosure.ScheduledEnd"/>, the end date the admin was shown. Returns
    /// whether it wrote. Whether the exercise is open is not decided here — the caller asks
    /// <see cref="ICheckingExerciseService"/> first; this only refuses to act on a row that changed.
    /// </summary>
    Task<bool> CloseExerciseEarlyAsync(ExerciseEarlyClosure closure, CancellationToken cancellationToken);
}

/// <summary>One early closure, as the repository writes it (AB#301022).</summary>
public sealed record ExerciseEarlyClosure
{
    public required Guid WindowId { get; init; }
    public required CheckingExerciseType Exercise { get; init; }

    /// <summary>The end date the exercise had when the admin confirmed. The write's guard.</summary>
    public required DateTime ScheduledEnd { get; init; }

    /// <summary>The exercise's new end date: a local wall-clock value, like every exercise date.</summary>
    public required DateTime NewEndDate { get; init; }

    /// <summary>The instant of the close in UTC — the audit row's Timestamp.</summary>
    public required DateTime ClosedAtUtc { get; init; }

    /// <summary>The sign-in subject of the admin who closed it.</summary>
    public required string UserId { get; init; }

    /// <summary>Their display name. The audit log has no user directory to look a subject up in.</summary>
    public required string ClosedByName { get; init; }
}

public class WindowDto
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public required DateTime EndDate { get; init; }
    public required KeyStages KeyStage { get; init; }
    public required CheckingWindowType CheckingWindowType { get; init; }
    public bool HasPupilData { get; init; }
    public required DateTime StartDate { get; init; }
}