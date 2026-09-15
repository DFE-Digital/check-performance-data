using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

/// <summary>
/// Outcome of deleting a request. A draft (InProgress / ReadyToSubmit) is hard-deleted;
/// a submitted request is soft-deleted (withdrawn). <see cref="PupilName"/> is carried so
/// the caller can show a confirmation message after redirecting. <see cref="RequestType"/>
/// is the deleted row's type (null when no row was found), so the caller can emit the
/// matching analytics event. <see cref="Deleted"/> is false when the request was left untouched:
/// a SubmittedCommitted row has already been dispatched to Zendesk, so withdrawing it would
/// change nothing downstream — the caller shows no banner and emits no event.
/// </summary>
public sealed record RequestDeletionResult(bool WasHardDeleted, string PupilName, RequestType? RequestType, bool Deleted = true)
{
    public static RequestDeletionResult Refused(string pupilName, RequestType? requestType) =>
        new(WasHardDeleted: false, pupilName, requestType, Deleted: false);
}
