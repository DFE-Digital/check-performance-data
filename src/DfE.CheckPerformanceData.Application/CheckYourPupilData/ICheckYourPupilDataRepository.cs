using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public interface ICheckYourPupilDataRepository
{
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize);
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize);
    Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId);
    Task<IReadOnlyList<PupilCsvDto>> GetAllIncludedPupilsAsync(Guid windowId, string laestab);
    Task<IReadOnlyList<PupilCsvDto>> GetAllNonIncludedPupilsAsync(Guid windowId, string laestab);
    Task<IReadOnlyList<PupilSuggestionDto>> SearchPupilsAsync(Guid windowId, string laestab, string urn, string query, PupilFilter filter, Guid? excludeId = null);
    Task<PupilDto> GetPupilAsync(Guid windowId, string laestab, Guid pupilId);
}
