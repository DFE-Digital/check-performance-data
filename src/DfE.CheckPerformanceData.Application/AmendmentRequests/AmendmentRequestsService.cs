using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class AmendmentRequestsService(
    ICheckYourPupilDataService checkYourPupilDataService,
    IRequestRepository requestRepository,
    ICheckingExerciseService checkingExercises,
    ICurrentUserService currentUserService,
    IRequestStateBlobClient requestStateBlobClient,
    ILogger<AmendmentRequestsService> logger, IEstablishmentViewContext? viewContext = null) : IAmendmentRequestsService
{
    public async Task<AmendmentRequestsResult> GetAmendmentRequestsAsync(Guid windowId)
    {
 var urn = long.TryParse(viewContext?.OrganisationUrn ?? currentUserService.OrganisationUrn, out var parsedUrn) ? parsedUrn : -1;
 var laestab = viewContext?.OrganisationLaestab ?? currentUserService.OrganisationLaestab;
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);
        var requests = await requestRepository.GetAmendmentRequestsAsync(windowId, urn);
        var submitted = await requestRepository.GetSubmittedRequestsAsync(windowId, urn);
        // The tab, and therefore the rows behind it, only exist on a window that runs the exercise.
        var hasResultsEnquiry = window.Exercises.Any(e => e.ExerciseType == CheckingExerciseType.ResultsEnquiry);
        var enquiries = hasResultsEnquiry
            ? await requestRepository.GetSubmittedResultsEnquiriesAsync(windowId, urn)
            : [];

        var issueRows = new List<ResultsEnquiryIssueDto>(enquiries.Count);
        foreach (var enquiry in enquiries)
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
                GradeText = GradeAchievedFor(state),
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
            // A display-only exercise has no kind, and this deadline row is keyed and labelled by
            // kind (ExerciseLabels.For), so it is left out here — same as every other kind lookup.
            // AB#307773: both are the signed-in school's, whose extension may run past the
            // exercise's own end.
            Deadlines = window.Exercises
                .InTabOrder()
                .Where(e => e.ExerciseType is not null)
                .Select(e => new ExerciseDeadlineDto
                {
                    // The Where above filters out the display-only exercises, so the kind is known
                    // here; the compiler cannot see across the LINQ chain, hence the suppression.
                    Exercise = e.ExerciseType!.Value,
                    EndDate = e.EndDateFor(laestab),
                    // The clock lives in one place, so "has this closed" is asked, never computed.
                    IsOpen = checkingExercises.IsOpen(window.Exercises, e.ExerciseType!.Value, laestab)
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
            HasResultsEnquiry = hasResultsEnquiry
        };
    }

    /// <summary>
    /// #712: the tab's Grade achieved value. The saved journey record is the only source: the
    /// revised grade the school chose, the grade it entered for the missing qualification, or the
    /// grade the DfE holds for the reported result. Undecidable degrades to "" — the row is the
    /// record of truth and the cell is enrichment. A present value is verbatim; only a missing,
    /// null or whitespace-only value shows as an empty cell.
    /// </summary>
    private static string GradeAchievedFor(RequestState? state)
    {
        var value = state?.SelectedWhatToChange switch
        {
            WhatToChange.IncorrectGrade => AnswerText(state, EnquiryQuestionIds.RevisedGrade),
            WhatToChange.MissingQualification => AnswerText(state, EnquiryQuestionIds.MissingGrade),
            WhatToChange.ResultDoesNotBelong => state?.SelectedResult?.Grade,
            _ => null
        };
        return string.IsNullOrWhiteSpace(value) ? "" : value;
    }

    private static string? AnswerText(RequestState? state, string questionId) =>
        state?.QuestionAnswers.GetValueOrDefault(questionId)?.TextValue;

    /// <summary>"Results enquiry - Missing qualification" → "Missing qualification". The suffix IS
    /// the user-facing kind label; falling back to the whole string keeps an unexpected format
    /// visible rather than blank.</summary>
    private static string EnquiryTypeLabel(string requestTypeDescription)
    {
        var separatorIndex = requestTypeDescription.IndexOf(" - ", StringComparison.Ordinal);
        return separatorIndex >= 0 ? requestTypeDescription[(separatorIndex + 3)..] : requestTypeDescription;
    }
}
