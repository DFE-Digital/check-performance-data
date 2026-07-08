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
    private static readonly int[] IncludedPinclCodes = [401, 403, 414, 421, 431];
    private static readonly TimeSpan CacheSlidingExpiry = TimeSpan.FromMinutes(30);

    public Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize)
        => GetPageAsync(windowId, laestab, included: true, search, page, pageSize);

    public Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize)
        => GetPageAsync(windowId, laestab, included: false, search, page, pageSize);

    public Task<IReadOnlyList<PupilCsvDto>> GetAllIncludedPupilsAsync(Guid windowId, string laestab)
        => GetAllAsync(windowId, laestab, included: true);

    public Task<IReadOnlyList<PupilCsvDto>> GetAllNonIncludedPupilsAsync(Guid windowId, string laestab)
        => GetAllAsync(windowId, laestab, included: false);

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

    private static bool IsIncluded(PupilRecord p) => IncludedPinclCodes.Contains(p.Pincl);

    private async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetPageAsync(Guid windowId, string laestab, bool included, string? search, int page, int pageSize)
    {
        var query = (await GetSchoolPupilsAsync(windowId, laestab))
            .Where(p => IsIncluded(p) == included);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Firstname.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                     p.Surname.Contains(search, StringComparison.OrdinalIgnoreCase));

        var ordered = query.OrderBy(p => p.Surname).ThenBy(p => p.Firstname).ToList();

        var items = ordered.Skip(page * pageSize).Take(pageSize).Select(ToPupilDto).ToList();

        return (items, ordered.Count);
    }

    private async Task<IReadOnlyList<PupilCsvDto>> GetAllAsync(Guid windowId, string laestab, bool included)
        => (await GetSchoolPupilsAsync(windowId, laestab))
            .Where(p => IsIncluded(p) == included)
            .OrderBy(p => p.Surname).ThenBy(p => p.Firstname)
            .Select(p => new PupilCsvDto
            {
                Upn = p.Upn,
                CypmdId = p.Cypmd_Id,
                Surname = p.Surname,
                Firstname = p.Firstname,
                Sex = p.Sex,
                DateOfBirth = p.DateOfBirth,
                Age = p.Age,
                Pincl = p.Pincl,
                Laestab = p.Laestab,
                Urn = p.Urn,
                EntryDate = p.EntryDate,
                SenF = p.SenF,
                FirstLanguage = p.FirstLanguage,
                Ethnicity = p.Ethnicity,
                ActualYearGroup = p.ActualYearGroup,
                NewMobile = p.NewMobile
            })
            .ToList();

    public async Task<IReadOnlyList<PupilSuggestionDto>> SearchPupilsAsync(Guid windowId, string laestab, string urn, string query, PupilFilter filter, Guid? excludeId = null)
    {
        var urnLong = long.Parse(urn);

        var pupils = (await GetSchoolPupilsAsync(windowId, laestab))
            .Where(p => filter switch
            {
                PupilFilter.All => true,
                PupilFilter.Included => IsIncluded(p),
                _ => !IsIncluded(p)
            })
            .Where(p => p.Upn.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Cypmd_Id.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Surname.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Firstname.Contains(query, StringComparison.OrdinalIgnoreCase));

        if (excludeId.HasValue)
            pupils = pupils.Where(p => p.Id != excludeId.Value);

        return pupils
            .OrderBy(p => p.Surname).ThenBy(p => p.Firstname)
            .Take(10)
            .Select(p => new PupilSuggestionDto(p.Id, $"{p.Surname}, {p.Firstname}, {p.DateOfBirth}"))
            .ToList();
    }

    private async Task<IReadOnlyList<PupilRecord>> GetSchoolPupilsAsync(Guid windowId, string laestab)
    {
        var key = $"pupils:{windowId}:{laestab}";
        if (cache.TryGetValue(key, out IReadOnlyList<PupilRecord>? cached) && cached is not null)
            return cached;

        var pupils = await pupilDataBlobClient.GetPupilsAsync(windowId, laestab) ?? [];
        cache.Set(key, pupils, new MemoryCacheEntryOptions { SlidingExpiration = CacheSlidingExpiry });
        return pupils;
    }

    private static PupilDto ToPupilDto(PupilRecord p) => new()
    {
        Id = p.Id,
        Surname = p.Surname,
        Firstname = p.Firstname,
        Sex = p.Sex,
        DateOfBirth = p.DateOfBirth,
        Age = p.Age,
        Cypmd_Id = p.Cypmd_Id,
        Upn = p.Upn,
        Pincl = p.Pincl
    };
}
