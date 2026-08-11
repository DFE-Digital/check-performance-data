using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class UrnAmendmentRequestsService(
    IRequestRepository requestRepository,
    IWindowRepository windowRepository,
    ICurrentUserService currentUserService) : IUrnAmendmentRequestsService
{
    public async Task<UrnAmendmentRequestsResult> GetAllSubmittedAmendmentRequestsAsync(CancellationToken cancellationToken)
    {
        
        long urn = long.Parse(currentUserService.OrganisationUrn);
        IReadOnlyList<SubmittedRequestData> submitted = await requestRepository.GetAllSubmittedRequestsAsync(urn);
        List<CheckingWindowDto> allWindows = await windowRepository.GetAllWindowsAsync(cancellationToken);
        List<OpenWindow> currentOpenWindows = allWindows
            .Where(w => w.IsOpen)
            .Select(w => new OpenWindow
            {
                WindowId = w.Id,
                WindowName = w.Title,
                WindowEndDate = w.EndDate
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


