using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

/// <remarks>
/// "Open" and the deadline are the signed-in school's: an exercise is open for the school until
/// the school's own end, which an extension may make later, and the window's stored end date is never read — it is the exercises' own latest end, the same for
/// every school. The one clock is ICheckingExerciseService's.
/// </remarks>
public class UrnAmendmentRequestsService(
    IRequestRepository requestRepository,
    IWindowRepository windowRepository,
    ICurrentUserService currentUserService,
    ICheckingExerciseService checkingExercises) : IUrnAmendmentRequestsService
{
    public async Task<UrnAmendmentRequestsResult> GetAllSubmittedAmendmentRequestsAsync(CancellationToken cancellationToken)
    {
        long urn = long.Parse(currentUserService.OrganisationUrn);
        string? laestab = currentUserService.OrganisationLaestab;
        IReadOnlyList<SubmittedRequestData> submitted = await requestRepository.GetAllSubmittedRequestsAsync(urn);
        List<CheckingWindowDto> allWindows = await windowRepository.GetAllWindowsAsync(cancellationToken);

        // A window is open for this school while any of its exercises is; its deadline is the
        // latest end this school sees across them (coarse, as this page has always been).
        List<OpenWindow> currentOpenWindows = allWindows
            .Where(w => checkingExercises.OpenCheckingExercises(w.Exercises, laestab).Count > 0)
            .Select(w => new OpenWindow
            {
                WindowId = w.Id,
                WindowName = w.Title,
                WindowEndDate = w.Exercises.Max(e => e.EndDateFor(laestab))
            })
            .ToList();

        return new UrnAmendmentRequestsResult
        {
            OpenWindows = currentOpenWindows,
            SubmittedRows = submitted.Select(r => new UrnAmendmentRequestDto
            {
                PupilName = PupilNameFormatter.Format(r.PupilFirstname, r.PupilSurname),
                RequestType = r.RequestType,
                RequestTypeDescription = r.RequestTypeDescription,
                ReferenceNumber = r.ReferenceNumber,
                Status = r.Status,
                Submitted = r.Submitted,
                WindowId = r.WindowId,
                WindowName = allWindows.Find(w => w.Id == r.WindowId)?.Title
            }).ToList()
        };
    }
}
