using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public interface ICheckYourPupilDataRepository
{
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize);
    Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize);
    Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId);
    Task<IReadOnlyList<PupilCsvDto>> GetAllIncludedPupilsAsync(Guid windowId, string laestab);
    Task<IReadOnlyList<PupilCsvDto>> GetAllNonIncludedPupilsAsync(Guid windowId, string laestab);
    Task<IReadOnlyList<PupilSuggestionDto>> SearchIncludedPupilsAsync(Guid windowId, string laestab, string query);
    Task<IReadOnlyList<PupilSuggestionDto>> SearchNonIncludedPupilsAsync(Guid windowId, string laestab, string query);
    Task<PupilDto> GetPupilAsync(Guid windowId, Guid pupilId);
}