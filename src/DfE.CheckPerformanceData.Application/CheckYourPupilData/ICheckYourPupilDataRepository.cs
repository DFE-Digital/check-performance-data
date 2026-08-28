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

    /// <summary>
    /// Autocomplete suggestions for the pupil search, capped at ten.
    ///
    /// <paramref name="cypmdIdAllowList"/> restricts the search to a set of students, and is how a
    /// results enquiry keeps a school from naming a student who holds no result. Null means no
    /// restriction (every other journey); an empty set correctly matches nobody. It is applied
    /// before the cap, so a student who does hold results is never crowded out by ten who do not.
    /// </summary>
    Task<IReadOnlyList<PupilSuggestionDto>> SearchPupilsAsync(Guid windowId, string laestab, string urn, string query, PupilFilter filter, Guid? excludeId = null, IReadOnlySet<string>? cypmdIdAllowList = null);

    Task<PupilDto> GetPupilAsync(Guid windowId, string laestab, Guid pupilId);
}
