using DfE.CheckPerformanceData.Application.Features.LandingPage;

namespace DfE.CheckPerformanceData.Application.Features.CheckYourPupilData;

public interface ICheckYourPupilDataService
{
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize);
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string? search, int page, int pageSize);
    Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId);
}