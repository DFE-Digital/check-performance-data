using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class AmendmentRequestsService(
    ICheckYourPupilDataService checkYourPupilDataService,
    IRequestRepository requestRepository,
    ICheckingExerciseService checkingExercises,
    ICurrentUserService currentUserService,
    IRequestStateBlobClient requestStateBlobClient,
    ILogger<AmendmentRequestsService> logger) : IAmendmentRequestsService
{
    public async Task<AmendmentRequestsResult> GetAmendmentRequestsAsync(Guid windowId, string? issueSearch = null)
    {
        var urn = long.Parse(currentUserService.OrganisationUrn);
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);
        var requests = await requestRepository.GetAmendmentRequestsAsync(windowId, urn);
        var submitted = await requestRepository.GetSubmittedRequestsAsync(windowId, urn);
        var enquiries = await requestRepository.GetSubmittedResultsEnquiriesAsync(windowId, urn);

        var term = issueSearch?.Trim();
        var matching = string.IsNullOrEmpty(term)
            ? enquiries
            : enquiries.Where(r =>
                    (r.PupilFirstname?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (r.PupilSurname?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

        // Filter before the blob loads: each surviving row costs one blob read, and a search
        // exists precisely to shrink the list.
        var issueRows = new List<ResultsEnquiryIssueDto>(matching.Count);
        foreach (var enquiry in matching)
        {
            RequestState? state = null;
            try
            {
                state = await requestStateBlobClient.GetAsync(windowId, enquiry.ReferenceNumber);
            }
            catch (Exception ex)
            {
                // A corrupt or unreachable blob degrades that row to empty enrichment cells rather
                // than failing the page: the row is the record of truth and must stay visible. Logged
                // by reference number only — never pupil data — so a run of these is visible without
                // becoming a PII leak in the logs.
                logger.LogWarning(ex,
                    "Failed to load the journey blob for results enquiry {ReferenceNumber}; Issues tab row will show empty enrichment cells",
                    enquiry.ReferenceNumber);
            }

            issueRows.Add(new ResultsEnquiryIssueDto
            {
                PupilName = PupilNameFormatter.Format(enquiry.PupilFirstname, enquiry.PupilSurname),
                Submitted = enquiry.Submitted,
                CypmdId = state?.SelectedPupil?.Cypmd_Id ?? "",
                TypeLabel = EnquiryTypeLabel(enquiry.RequestTypeDescription),
                // A journey stores exactly one subject: SelectedQualification for missing
                // qualification, SelectedResult for the other two kinds — so coalescing is safe.
                QualificationText = state?.SelectedQualification?.QualificationTitle
                    ?? state?.SelectedResult?.QualificationName
                    ?? "",
                ReferenceNumber = enquiry.ReferenceNumber
            });
        }

        return new AmendmentRequestsResult
        {
            WindowTitle = window.Title,
            LearnerNoun = LearnerNoun.For(window.CheckingWindowType),
            // #320: a deadline per exercise the window runs, not the outer window's end date. The
            // grid lists both populations, and on a 16-19 window pupil data checking shuts months
            // before results enquiry does — one date could only ever be right for one of them.
            Deadlines = window.Exercises
                .OrderBy(e => e.SortOrder)
                .Select(e => new ExerciseDeadlineDto
                {
                    Exercise = e.ExerciseType,
                    EndDate = e.EndDate,
                    // The clock lives in one place, so "has this closed" is asked, never computed.
                    IsOpen = checkingExercises.IsOpen(window.Exercises, e.ExerciseType)
                })
                .ToList(),
            Rows = requests.Select(r => new AmendmentRequestDto
            {
                PupilName = PupilNameFormatter.Format(r.PupilFirstname, r.PupilSurname),
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                Status = r.Status,
                ReferenceNumber = r.ReferenceNumber
            }).ToList(),
            SubmittedRows = submitted.Select(r => new SubmittedRequestDto
            {
                PupilName = PupilNameFormatter.Format(r.PupilFirstname, r.PupilSurname),
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                ReferenceNumber = r.ReferenceNumber,
                Status = r.Status,
                Submitted = r.Submitted
            }).ToList(),
            IssueRows = issueRows,
            HasAnyIssues = enquiries.Count > 0
        };
    }

    /// <summary>"Results enquiry - Missing qualification" → "Missing qualification". The suffix IS
    /// the user-facing kind label; falling back to the whole string keeps an unexpected format
    /// visible rather than blank.</summary>
    private static string EnquiryTypeLabel(string requestTypeDescription)
    {
        var separatorIndex = requestTypeDescription.IndexOf(" - ", StringComparison.Ordinal);
        return separatorIndex >= 0 ? requestTypeDescription[(separatorIndex + 3)..] : requestTypeDescription;
    }
}
