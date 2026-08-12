using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class AmendmentRequestsService(
    ICheckYourPupilDataService checkYourPupilDataService,
    IRequestRepository requestRepository,
    ICheckingExerciseService checkingExercises,
    ICurrentUserService currentUserService) : IAmendmentRequestsService
{
    public async Task<AmendmentRequestsResult> GetAmendmentRequestsAsync(Guid windowId)
    {
        var urn = long.Parse(currentUserService.OrganisationUrn);
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);
        var requests = await requestRepository.GetAmendmentRequestsAsync(windowId, urn);
        var submitted = await requestRepository.GetSubmittedRequestsAsync(windowId, urn);

        return new AmendmentRequestsResult
        {
            WindowTitle = window.Title,
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
                Submitted = r.Submitted,
            }).ToList()
        };
    }
}
