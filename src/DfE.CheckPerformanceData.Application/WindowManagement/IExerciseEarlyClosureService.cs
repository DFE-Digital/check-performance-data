using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// AB#301022: closes one open checking exercise before its scheduled end, so schools can no longer
/// act on it, and records who did it.
/// </summary>
/// <remarks>
/// "Closed early" is not a new state. The exercise's end date is moved to the moment of the close,
/// so every gate that already asks <see cref="ICheckingExerciseService"/> — the landing page, Check
/// your pupil data, every journey action — shuts with no rule of its own, and a manual close lands
/// in exactly the state a scheduled close does.
///
/// This is not <see cref="ICloseExerciseService"/>. That one hands an exercise's requests over for
/// processing and is deliberately blind to dates; this one is all about dates and touches no
/// request. The admin Close action calls this first and that second.
/// </remarks>
public interface IExerciseEarlyClosureService
{
    /// <summary>
    /// Runs the pre-close checks (the window runs the exercise; the exercise is open right now)
    /// and, if they pass, closes it. The caller's permission is the route's concern, not this one's.
    /// </summary>
    Task<EarlyClosureResult> CloseEarlyAsync(
        Guid windowId, CheckingExerciseType exercise, EarlyClosureActor actor, CancellationToken cancellationToken);
}

/// <summary>Who is closing: the sign-in subject for the audit row's UserId, and the name it shows.</summary>
public sealed record EarlyClosureActor(string UserId, string DisplayName);

public enum EarlyClosureStatus
{
    /// <summary>The exercise was open and is now closed.</summary>
    Closed,

    /// <summary>Not open — not started, already ended, or changed by someone else in the meantime. Nothing was written.</summary>
    NotOpen,

    /// <summary>No such window, or the window does not run that exercise. Nothing was written.</summary>
    NotFound
}

public sealed record EarlyClosureResult
{
    public required EarlyClosureStatus Status { get; init; }

    /// <summary>When it was closed, on the local wall clock, to the second. Only set when Closed.</summary>
    public DateTime ClosedAt { get; init; }

    /// <summary>The end date the exercise had before the close. Only set when Closed.</summary>
    public DateTime ScheduledEnd { get; init; }

    public static EarlyClosureResult NotFound { get; } = new() { Status = EarlyClosureStatus.NotFound };

    public static EarlyClosureResult NotOpen { get; } = new() { Status = EarlyClosureStatus.NotOpen };

    public static EarlyClosureResult Closed(DateTime closedAt, DateTime scheduledEnd) =>
        new() { Status = EarlyClosureStatus.Closed, ClosedAt = closedAt, ScheduledEnd = scheduledEnd };
}
