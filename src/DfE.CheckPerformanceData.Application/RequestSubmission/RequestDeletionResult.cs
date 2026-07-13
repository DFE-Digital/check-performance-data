using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

/// <summary>
/// Outcome of deleting a request. A draft (InProgress / ReadyToSubmit) is hard-deleted;
/// a submitted request is soft-deleted (withdrawn). <see cref="PupilName"/> is carried so
/// the caller can show a confirmation message after redirecting. <see cref="RequestType"/>
/// is the deleted row's type (null when no row was found), so the caller can emit the
/// matching analytics event.
/// </summary>
public sealed record RequestDeletionResult(bool WasHardDeleted, string PupilName, RequestType? RequestType);
