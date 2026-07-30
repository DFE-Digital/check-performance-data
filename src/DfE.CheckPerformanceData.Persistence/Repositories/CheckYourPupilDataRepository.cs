using System.Globalization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class CheckYourPupilDataRepository(
    IPortalDbContext dbContext,
    IPupilDataBlobClient pupilDataBlobClient,
    IMemoryCache cache) : ICheckYourPupilDataRepository
{
    private static readonly TimeSpan CacheSlidingExpiry = TimeSpan.FromMinutes(30);

    public async Task<(IReadOnlyList<IPupilRecord> Items, int TotalCount)> GetPupilPageAsync(
        Guid windowId, string laestab, bool included, string? search, int page, int pageSize)
    {
        var ordered = await GetPopulationAsync(windowId, laestab, included, search);
        var items = ordered.Skip(page * pageSize).Take(pageSize).ToList();
        return (items, ordered.Count);
    }

    public async Task<IReadOnlyList<IPupilRecord>> GetAllPupilsAsync(Guid windowId, string laestab, bool included)
        => await GetPopulationAsync(windowId, laestab, included, search: null);

    private async Task<List<IPupilRecord>> GetPopulationAsync(Guid windowId, string laestab, bool included, string? search)
    {
        var query = (await GetSchoolPupilsAsync(windowId, laestab))
            .Where(p => p.IsIncluded == included);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Firstname.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                     p.Surname.Contains(search, StringComparison.OrdinalIgnoreCase));

        return query.OrderBy(p => p.Surname).ThenBy(p => p.Firstname).ToList();
    }

    public async Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId)
        => await dbContext.CheckingWindows
            .AsNoTracking()
            .Where(w => w.Id == windowId)
            .Select(w => new CheckingWindowDto { EndDate = w.EndDate, Title = w.Title, KeyStage = w.KeyStage, CheckingWindowType = w.CheckingWindowType, StartDate = w.StartDate })
            .SingleAsync();

    public async Task<PupilDto> GetPupilAsync(Guid windowId, string laestab, Guid pupilId)
    {
        var pupils = await GetSchoolPupilsAsync(windowId, laestab);
        return ToPupilDto(pupils.Single(p => p.Id == pupilId));
    }

    public async Task<IReadOnlyList<PupilSuggestionDto>> SearchPupilsAsync(Guid windowId, string laestab, string urn, string query, PupilFilter filter, Guid? excludeId = null)
    {
        // urn is retained on the signature for callers but is unused: the UPN-based exclusion
        // query it served was removed in 3f9efadf, which moved conflict detection onto pupil Id.
        var pupils = (await GetSchoolPupilsAsync(windowId, laestab))
            .Where(p => filter switch
            {
                PupilFilter.All => true,
                PupilFilter.Included => p.IsIncluded,
                _ => !p.IsIncluded
            })
            .Where(p => p.Identifier.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Cypmd_Id.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Surname.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Firstname.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (excludeId.HasValue)
            pupils = pupils.Where(p => p.Id != excludeId.Value);

        return pupils
            .OrderBy(p => p.Surname).ThenBy(p => p.Firstname)
            .Take(10)
            .Select(p => new PupilSuggestionDto(p.Id, $"{p.Surname}, {p.Firstname}, {PupilDateFormatter.ToDisplayDate(p.DateOfBirth)}"))
            .ToList();
    }

    private async Task<IReadOnlyList<IPupilRecord>> GetSchoolPupilsAsync(Guid windowId, string laestab)
    {
        var key = $"pupils:{windowId}:{laestab}";
        if (cache.TryGetValue(key, out IReadOnlyList<IPupilRecord>? cached) && cached is not null)
            return cached;

        // The blob's record shape depends on the window type, so the window is resolved first.
        var window = await GetCheckingWindowAsync(windowId);
        var pupils = await pupilDataBlobClient.GetPupilsAsync(windowId, laestab, window.CheckingWindowType) ?? [];
        cache.Set(key, pupils, new MemoryCacheEntryOptions { SlidingExpiration = CacheSlidingExpiry });
        return pupils;
    }

    private static PupilDto ToPupilDto(IPupilRecord p) => new()
    {
        Id = p.Id,
        Surname = p.Surname,
        Firstname = p.Firstname,
        Sex = p.Sex,
        DateOfBirth = PupilDateFormatter.ToDisplayDate(p.DateOfBirth),
        Age = p.Age,
        Cypmd_Id = p.Cypmd_Id,
        Identifier = p.Identifier,
        Pincl = p.Pincl ?? 0
    };
}
