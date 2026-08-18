using System.Globalization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
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
        //
        // AB#297004: matching and label text differ by window type (16-19 searches on date of birth
        // too and shows its identifiers), so both live in PupilSuggestionFormat where they can be
        // unit-tested against pinned copy.
        var (pupils0, windowType) = await GetSchoolPupilsWithWindowTypeAsync(windowId, laestab);
        var pupils = pupils0
            .Where(p => filter switch
            {
                PupilFilter.All => true,
                PupilFilter.Included => p.IsIncluded,
                _ => !p.IsIncluded
            })
            .Where(p => PupilSuggestionFormat.Matches(p, query, windowType));

        if (excludeId.HasValue)
            pupils = pupils.Where(p => p.Id != excludeId.Value);

        return pupils
            .OrderBy(p => p.Surname).ThenBy(p => p.Firstname)
            .Take(10)
            .Select(p => new PupilSuggestionDto(p.Id, PupilSuggestionFormat.Label(p, windowType)))
            .ToList();
    }

    private sealed record SchoolPupilsCacheEntry(IReadOnlyList<IPupilRecord> Pupils, CheckingWindowType WindowType);

    private async Task<IReadOnlyList<IPupilRecord>> GetSchoolPupilsAsync(Guid windowId, string laestab)
        => (await GetSchoolPupilsWithWindowTypeAsync(windowId, laestab)).Pupils;

    private async Task<SchoolPupilsCacheEntry> GetSchoolPupilsWithWindowTypeAsync(Guid windowId, string laestab)
    {
        var key = $"pupils:{windowId}:{laestab}";
        if (cache.TryGetValue(key, out SchoolPupilsCacheEntry? cached) && cached is not null)
            return cached;

        // The blob's record shape depends on the window type, so the window is resolved first.
        var window = await GetCheckingWindowAsync(windowId);
        var pupils = await pupilDataBlobClient.GetPupilsAsync(windowId, laestab, window.CheckingWindowType) ?? [];
        var entry = new SchoolPupilsCacheEntry(pupils, window.CheckingWindowType);
        cache.Set(key, entry, new MemoryCacheEntryOptions { SlidingExpiration = CacheSlidingExpiry });
        return entry;
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
        Pincl = p.Pincl ?? 0,
        MatchRef = p.MatchRef,
        Laestab = p.Laestab,
        EntryDate = p.EntryDate
    };
}
