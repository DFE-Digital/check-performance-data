using DfE.CheckPerformanceData.Application.AmendmentRequests;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

public interface IRequestRepository
{
    Task<DuplicateCheckResult> CheckForConflictAsync(Guid windowId, Guid pupilId, long organisationUrn, string currentReferenceNumber, Guid currentUserId);

    /// <summary>Returns the reference number of a submitted request for the given pupil, or null if none exists.</summary>
    Task<string?> HasSubmittedRequestAsync(Guid windowId, Guid pupilId, long organisationUrn);
    /// <returns>The Id of the inserted or updated <c>ChangeRequests</c> row.</returns>
    /// <exception cref="DuplicateEnquiryException">A submitted results enquiry with a subject, when the
    /// school already has another submitted enquiry for the same student and subject.</exception>
    Task<Guid> UpsertAsync(ChangeRequestData data);

    /// <summary>
    /// Returns the reference number of the newest submitted results enquiry for the student and
    /// subject at the school in the window, or null when there is none. A withdrawn enquiry and a
    /// row with no subject do not count.
    /// </summary>
    /// <param name="excludedReferenceNumber">The caller's own request, which is never a duplicate of itself.</param>
    Task<string?> FindSubmittedResultsEnquiryReferenceAsync(
        Guid windowId, long organisationUrn, Guid pupilId, EnquirySubject subject,
        string? excludedReferenceNumber, CancellationToken ct = default);
    Task<IReadOnlyList<AmendmentRequestData>> GetAmendmentRequestsAsync(Guid windowId, long organisationUrn);
    Task<IReadOnlyList<SubmittedRequestData>> GetSubmittedRequestsAsync(Guid windowId, long organisationUrn);
    Task<IReadOnlyList<SubmittedRequestData>> GetAllSubmittedRequestsAsync(long organisationUrn);

    /// <summary>
    /// The school's submitted results enquiries for the window, newest first (AB#298325 Issues tab).
    /// The complement of <see cref="GetSubmittedRequestsAsync"/>, which deliberately excludes
    /// enquiry rows — the two must never overlap or a row shows on both tabs.
    /// </summary>
    Task<IReadOnlyList<SubmittedRequestData>> GetSubmittedResultsEnquiriesAsync(Guid windowId, long organisationUrn);
    Task<AmendmentRequestData?> GetAmendmentRequestAsync(Guid windowId, long organisationUrn, string referenceNumber);
    Task<ConfirmDataCorrectData?> GetConfirmDataCorrectAsync(Guid windowId, long organisationUrn, string referenceNumber);

    /// <summary>
    /// Soft-deletes a request by setting its status to <see cref="Domain.Enums.RequestStatus.Withdrawn"/>
    /// and recording who withdrew it and when.
    /// Scoped by window + org + reference so a school cannot withdraw another school's request.
    /// </summary>
    Task WithdrawAsync(Guid windowId, long organisationUrn, string referenceNumber, string withdrawnByEmail, DateTime withdrawnAt);

    /// <summary>
    /// Hard-deletes a request row. Used for in-progress / ready-to-submit drafts.
    /// Scoped by window + org + reference so a school cannot delete another school's request.
    /// </summary>
    Task DeleteAsync(Guid windowId, long organisationUrn, string referenceNumber);

    /// <summary>
    /// Returns the distinct pupil ids that already have a submitted (Submitted)
    /// request for the window/org. Used to flag bulk-selected drafts whose pupil is already submitted.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetSubmittedPupilIdsAsync(Guid windowId, long organisationUrn);
}
