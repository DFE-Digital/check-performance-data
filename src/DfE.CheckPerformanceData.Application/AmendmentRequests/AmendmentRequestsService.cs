using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.RequestSubmission;

namespace DfE.CheckPerformanceData.Application.AmendmentRequests;

public sealed class AmendmentRequestsService(
    ICheckYourPupilDataService checkYourPupilDataService,
    IRequestRepository requestRepository,
    ICurrentUserService currentUserService) : IAmendmentRequestsService
{
    public async Task<AmendmentRequestsResult> GetAmendmentRequestsAsync(Guid windowId)
    {
        var urn = long.Parse(currentUserService.OrganisationUrn);
        var window = await checkYourPupilDataService.GetCheckingWindowAsync(windowId);
        var requests = await requestRepository.GetAmendmentRequestsAsync(windowId, urn);

        return new AmendmentRequestsResult
        {
            WindowEndDate = window.EndDate,
            Rows = requests.Select(r => new AmendmentRequestDto
            {
                PupilName = BuildPupilName(r.PupilFirstname, r.PupilSurname),
                RequestType = r.RequestType,
                Status = r.Status,
                ReferenceNumber = r.ReferenceNumber
            }).ToList()
        };
    }

    private static string BuildPupilName(string? firstname, string? surname)
    {
        var name = $"{firstname} {surname}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "N/A" : name;
    }
}
