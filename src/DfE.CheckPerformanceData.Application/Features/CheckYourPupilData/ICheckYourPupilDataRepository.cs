namespace DfE.CheckPerformanceData.Application.Features.CheckYourPupilData;

public interface ICheckYourPupilDataRepository
{
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize);
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize);
}
