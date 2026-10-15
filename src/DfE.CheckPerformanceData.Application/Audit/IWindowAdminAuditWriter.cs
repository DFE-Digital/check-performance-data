using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Audit;

/// <summary>
/// Writes the hand-written WindowAdmin audit rows that have no transaction of their own to ride
/// in (AB#302158). An early closure is not written here: its row belongs inside the guarded
/// end-date move, in WindowRepository.CloseExerciseEarlyAsync.
/// </summary>
public interface IWindowAdminAuditWriter
{
    /// <summary>
    /// Records that the service handed an exercise's requests over by itself. One
    /// WindowAdmin / RequestsSentAutomatically row, EntityId = the window id, no user.
    /// </summary>
    Task RecordAutomaticHandOverAsync(AutomaticHandOverAudit record, CancellationToken cancellationToken);
}

/// <summary>One automatic hand-over, as the audit row records it. No pupil data.</summary>
public sealed record AutomaticHandOverAudit
{
    public required Guid WindowId { get; init; }
    public required string WindowTitle { get; init; }
    public required Guid ExerciseId { get; init; }
    public required CheckingExerciseType Exercise { get; init; }

    /// <summary>The exercise's end date: a local wall-clock value, like every exercise date.</summary>
    public required DateTime ExerciseEnd { get; init; }

    public required int RequestsSent { get; init; }
    public required int DraftsCancelled { get; init; }

    /// <summary>The instant of the run in UTC — the audit row's Timestamp.</summary>
    public required DateTime RanAtUtc { get; init; }
}
