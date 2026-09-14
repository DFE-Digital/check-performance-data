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
    Task<bool> TrySetStatusAsync(Guid runId, EgressRunStatus from, EgressRunStatus to, CancellationToken ct);
    Task MarkPreprocessingFailedAsync(Guid runId, IReadOnlyList<EgressRecordFailure> failures, CancellationToken ct);
    Task SavePreprocessedAsync(Guid runId, IReadOnlyList<NewLearnerRow> newLearners, IReadOnlyList<RemoveLearnerRow> removeLearners, DateOnly exportDate, IReadOnlyDictionary<EgressOutputType, string> fileNames, CancellationToken ct);
    Task<IReadOnlyList<NewLearnerRow>> GetNewLearnersAsync(Guid runId, CancellationToken ct);
    Task<IReadOnlyList<RemoveLearnerRow>> GetRemoveLearnersAsync(Guid runId, CancellationToken ct);
    /// <summary>Re-activates a TransferFailed run's outputs for a retry; returns the blocker if another run now holds the pair.</summary>
    Task<EgressBlocker?> TryReactivateAsync(Guid runId, CancellationToken ct);
    Task MarkTransferredAsync(Guid runId, EgressTransferAudit audit, DateTime transferredAtUtc, CancellationToken ct);
    Task MarkTransferFailedAsync(Guid runId, string reason, string userId, CancellationToken ct);
    Task AbandonAsync(Guid runId, CancellationToken ct);
}
