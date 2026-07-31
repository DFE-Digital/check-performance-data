using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public interface ICheckYourPupilDataRepository
{
    /// <summary>One page of a school's pupils, filtered to the included or non-included population.</summary>
    Task<(IReadOnlyList<IPupilRecord> Items, int TotalCount)> GetPupilPageAsync(
        Guid windowId, string laestab, bool included, string? search, int page, int pageSize);

    /// <summary>Every pupil in a population, ordered as the table orders them. Feeds the CSV export.</summary>
    Task<IReadOnlyList<IPupilRecord>> GetAllPupilsAsync(Guid windowId, string laestab, bool included);

    Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId);
    Task<IReadOnlyList<PupilSuggestionDto>> SearchPupilsAsync(Guid windowId, string laestab, string urn, string query, PupilFilter filter, Guid? excludeId = null);
    Task<PupilDto> GetPupilAsync(Guid windowId, string laestab, Guid pupilId);
}
