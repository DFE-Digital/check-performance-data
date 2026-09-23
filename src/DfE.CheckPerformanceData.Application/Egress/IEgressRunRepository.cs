using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

public sealed record EgressBlocker(Guid RunId, EgressRunStatus Status, string StartedByName, DateTime StartedAtUtc, DateTime? TransferredAtUtc, string? TransferredByName);
public sealed record EgressCandidateRequest(Guid ChangeRequestId, string ReferenceNumber, string? CrmId, long OrganisationUrn, string? OrganisationLaestab, DateTime SubmittedAtUtc, RequestStatus Status);
public sealed record EgressRunCreate(Guid WindowId, Guid StartedById, string StartedByName, string? StartedByEmail, IReadOnlyList<EgressRunOutputCreate> Outputs);
public sealed record EgressRunOutputCreate(EgressOutputType OutputType, IReadOnlyList<EgressSourceRecord> Records);
public sealed record EgressRunOutputDto(Guid Id, EgressOutputType OutputType, bool IsActive, IReadOnlyList<EgressSourceRecord> Records, int SourceRecordCount, int? OutputRecordCount, string? FileName, string? Sha256);
public sealed record EgressRunDto(Guid Id, Guid WindowId, EgressRunStatus Status, Guid StartedById, string StartedByName, DateTime StartedAtUtc, DateTime? PreprocessedAtUtc, DateOnly? ExportDate, DateTime? TransferredAtUtc, string? TransferredByName, IReadOnlyList<EgressRecordFailure> Failures, string? TransferFailureReason, IReadOnlyList<EgressRunOutputDto> Outputs);
public sealed record EgressRunListItem(Guid Id, Guid WindowId, string WindowTitle, EgressRunStatus Status, IReadOnlyList<EgressOutputType> OutputTypes, string StartedByName, DateTime StartedAtUtc, DateTime? TransferredAtUtc);
/// <summary>Runs history filters (AB#294590); null means "all". They are cumulative.</summary>
public sealed record EgressRunHistoryFilter(Guid? WindowId, EgressRunOutcome? Outcome);
/// <summary>One history row. RecordsTransferred is what LDS received: the saved row count for a Transferred run, 0 for every other status.</summary>
public sealed record EgressRunHistoryRow(Guid Id, Guid WindowId, string WindowTitle, EgressRunStatus Status, IReadOnlyList<EgressOutputType> OutputTypes, int RecordsTransferred, string StartedByName, DateTime StartedAtUtc);
/// <summary>One page of the history. Page is 1-based and already clamped to [1, TotalPages].</summary>
public sealed record EgressRunHistoryPage(IReadOnlyList<EgressRunHistoryRow> Rows, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));
}
public sealed record EgressTransferAudit(string UserId, string UserName, string TargetContainer, IReadOnlyDictionary<EgressOutputType, (string FileName, int Records, string Sha256)> Files);
public sealed class EgressRunConflictException(string message) : Exception(message);

/// <summary>
/// Persistence for egress runs (AB#294553). Two rules the implementation must keep: (1) the
/// concurrency lock is the database's partial unique index, so CreateRunAsync and
/// TryReactivateAsync translate a 23505 into EgressRunConflictException / a blocker rather than
/// pre-checking only; (2) every status change that ends a run (failed, abandoned) clears IsActive
/// on its outputs in the same statement, and a successful transfer leaves them active for good.
/// </summary>
public interface IEgressRunRepository
{
    Task<EgressBlocker?> FindBlockerAsync(Guid windowId, EgressOutputType outputType, CancellationToken ct);
    Task<IReadOnlyList<EgressCandidateRequest>> GetCandidateRequestsAsync(Guid windowId, WhatToChange amendmentType, CancellationToken ct);
    /// <exception cref="EgressRunConflictException">Another active run holds one of the (window, output type) pairs.</exception>
    Task<Guid> CreateRunAsync(EgressRunCreate create, CancellationToken ct);
    Task<EgressRunDto?> GetRunAsync(Guid runId, CancellationToken ct);
    Task<IReadOnlyList<EgressRunListItem>> ListRunsAsync(CancellationToken ct);
    /// <summary>Runs history (AB#294590): filtered, newest StartedAtUtc first (Id descending on a tie), one page. An out-of-range page clamps; a pageSize below 1 is treated as 1.</summary>
    Task<EgressRunHistoryPage> ListHistoryAsync(EgressRunHistoryFilter filter, int page, int pageSize, CancellationToken ct);
    Task<bool> TrySetStatusAsync(Guid runId, EgressRunStatus from, EgressRunStatus to, CancellationToken ct);
    /// <summary>M4: guarded by <paramref name="expectedStatus"/>; returns rows affected (0 = lost the race — e.g. the run was abandoned in the meantime — nothing was written).</summary>
    Task<int> MarkPreprocessingFailedAsync(Guid runId, EgressRunStatus expectedStatus, IReadOnlyList<EgressRecordFailure> failures, CancellationToken ct);
    /// <summary>M4: guarded by <paramref name="expectedStatus"/>; returns rows affected (0 = lost the race — nothing was saved).</summary>
    Task<int> SavePreprocessedAsync(Guid runId, EgressRunStatus expectedStatus, IReadOnlyList<NewLearnerRow> newLearners, IReadOnlyList<RemoveLearnerRow> removeLearners, DateOnly exportDate, IReadOnlyDictionary<EgressOutputType, string> fileNames, CancellationToken ct);
    Task<IReadOnlyList<NewLearnerRow>> GetNewLearnersAsync(Guid runId, CancellationToken ct);
    Task<IReadOnlyList<RemoveLearnerRow>> GetRemoveLearnersAsync(Guid runId, CancellationToken ct);
    /// <summary>Re-activates a TransferFailed run's outputs for a retry; returns the output type
    /// and blocker if another run now holds one of the run's pairs.</summary>
    Task<(EgressOutputType OutputType, EgressBlocker Blocker)?> TryReactivateAsync(Guid runId, CancellationToken ct);
    /// <summary>M4: guarded by <paramref name="expectedStatus"/>; returns rows affected (0 = lost the race — no Succeeded audit row is written).</summary>
    Task<int> MarkTransferredAsync(Guid runId, EgressRunStatus expectedStatus, EgressTransferAudit audit, DateTime transferredAtUtc, CancellationToken ct);
    /// <summary>M4: guarded by <paramref name="expectedStatus"/>; returns rows affected (0 = the run had already moved on, e.g. to Abandoned — nothing was overwritten).</summary>
    Task<int> MarkTransferFailedAsync(Guid runId, EgressRunStatus expectedStatus, string reason, string userId, CancellationToken ct);
    /// <summary>Excludes only Transferred; admits Preprocessing/Transferring so a stuck run can always be released. Returns rows affected (0 = already Transferred).</summary>
    Task<int> AbandonAsync(Guid runId, CancellationToken ct);
}
