using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public class CheckYourPupilDataRepository(IPortalDbContext dbContext) : ICheckYourPupilDataRepository
{
    private static readonly int[] IncludedPinclCodes = [401, 403, 414, 421, 431];

    public async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize)
        => await GetPageAsync(windowId, laestab, included: true, search, page, pageSize);

    public async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string laestab, string? search, int page, int pageSize)
        => await GetPageAsync(windowId, laestab, included: false, search, page, pageSize);

    public async Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId)
        => await dbContext.CheckingWindows
            .AsNoTracking()
            .Where(w => w.Id == windowId)
            .Select(w => new CheckingWindowDto{EndDate = w.EndDate, Title = w.Title, KeyStage = w.KeyStage, StartDate = w.StartDate})
            .SingleAsync();

    public async Task<IReadOnlyList<PupilCsvDto>> GetAllIncludedPupilsAsync(Guid windowId, string laestab)
        => await GetAllAsync(windowId, laestab, included: true);

    public async Task<IReadOnlyList<PupilCsvDto>> GetAllNonIncludedPupilsAsync(Guid windowId, string laestab)
        => await GetAllAsync(windowId, laestab, included: false);

    private async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetPageAsync(Guid windowId, string laestab, bool included, string? search, int page, int pageSize)
    {
        var query = dbContext.Pupils
            .AsNoTracking()
            .Where(p => p.CheckingWindowId == windowId && p.Laestab == laestab &&
                        (included ? IncludedPinclCodes.Contains(p.Pincl) : !IncludedPinclCodes.Contains(p.Pincl)));

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => EF.Functions.ILike(p.Firstname, $"%{search}%") ||
                                     EF.Functions.ILike(p.Surname, $"%{search}%"));

        query = query.OrderBy(p => p.Surname).ThenBy(p => p.Firstname);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip(page * pageSize)
            .Take(pageSize)
            .Select(p => new PupilDto
            {
                Id = p.Id,
                Surname = p.Surname,
                Firstname = p.Firstname,
                Sex = p.Sex,
                DateOfBirth = p.DateOfBirth,
                Age = p.Age,
                FirstLanguage = p.FirstLanguage
            })
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<PupilSuggestionDto>> SearchIncludedPupilsAsync(Guid windowId, string laestab, string query)
        => await dbContext.Pupils
            .AsNoTracking()
            .Where(p => p.CheckingWindowId == windowId && p.Laestab == laestab &&
                        IncludedPinclCodes.Contains(p.Pincl) &&
                        (EF.Functions.ILike(p.Surname, $"%{query}%") ||
                         EF.Functions.ILike(p.Firstname, $"%{query}%")))
            .OrderBy(p => p.Surname).ThenBy(p => p.Firstname)
            .Take(10)
            .Select(p => new PupilSuggestionDto(p.Id, $"{p.Surname}, {p.Firstname}, {p.DateOfBirth}"))
            .ToListAsync();

    public async Task<IReadOnlyList<PupilSuggestionDto>> SearchNonIncludedPupilsAsync(Guid windowId, string laestab, string query)
        => await dbContext.Pupils
            .AsNoTracking()
            .Where(p => p.CheckingWindowId == windowId && p.Laestab == laestab &&
                        !IncludedPinclCodes.Contains(p.Pincl) &&
                        (EF.Functions.ILike(p.Surname, $"%{query}%") ||
                         EF.Functions.ILike(p.Firstname, $"%{query}%")))
            .OrderBy(p => p.Surname).ThenBy(p => p.Firstname)
            .Take(10)
            .Select(p => new PupilSuggestionDto(p.Id, $"{p.Surname}, {p.Firstname}, {p.DateOfBirth}"))
            .ToListAsync();

    public async Task<PupilDto> GetPupilAsync(Guid windowId, Guid pupilId)
    {
        return await dbContext.Pupils
            .AsNoTracking()
            .Where(p => p.CheckingWindowId == windowId && p.Id == pupilId)
            .Select(p => new PupilDto
            {
                Firstname = p.Firstname,
                Surname = p.Surname,
                Id = p.Id,
                DateOfBirth = p.DateOfBirth,
                Sex = p.Sex,
                FirstLanguage = p.FirstLanguage,
                Age = p.Age
            })
            .SingleAsync();
    }

    private async Task<IReadOnlyList<PupilCsvDto>> GetAllAsync(Guid windowId, string laestab, bool included)
        => await dbContext.Pupils
            .AsNoTracking()
            .Where(p => p.CheckingWindowId == windowId && p.Laestab == laestab &&
                        (included ? IncludedPinclCodes.Contains(p.Pincl) : !IncludedPinclCodes.Contains(p.Pincl)))
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
            .ToListAsync();
}
