using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class CheckYourPupilDataRepository(IPortalDbContext dbContext) : ICheckYourPupilDataRepository
{
    private static readonly int[] IncludedPinclCodes = [401, 403, 414, 421, 431];

    public Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetIncludedPupilsAsync(Guid windowId, string urn, string? search, int page, int pageSize)
        => GetPageAsync(windowId, urn, included: true, search, page, pageSize);

    public Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetNonIncludedPupilsAsync(Guid windowId, string urn, string? search, int page, int pageSize)
        => GetPageAsync(windowId, urn, included: false, search, page, pageSize);

    public Task<IReadOnlyList<PupilCsvDto>> GetAllIncludedPupilsAsync(Guid windowId, string urn)
        => GetAllAsync(windowId, urn, included: true);

    public Task<IReadOnlyList<PupilCsvDto>> GetAllNonIncludedPupilsAsync(Guid windowId, string urn)
        => GetAllAsync(windowId, urn, included: false);

    public Task<IReadOnlyList<PupilSuggestionDto>> SearchPupilsAsync(Guid windowId, string urn, string query, bool included)
        => SearchAsync(windowId, urn, query, included);

    public async Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId)
        => await dbContext.CheckingWindows
            .AsNoTracking()
            .Where(w => w.Id == windowId)
            .Select(w => new CheckingWindowDto { EndDate = w.EndDate, Title = w.Title, KeyStage = w.KeyStage, StartDate = w.StartDate })
            .SingleAsync();

    public async Task<PupilDto> GetPupilAsync(Guid windowId, Guid pupilId)
        => await dbContext.Pupils
            .AsNoTracking()
            .Where(p => p.CheckingWindowId == windowId && p.Id == pupilId)
            .Select(ToPupilDto)
            .SingleAsync();

    private IQueryable<Pupil> BaseQuery(Guid windowId, string urn, bool included)
        => dbContext.Pupils
            .AsNoTracking()
            .Where(p => p.CheckingWindowId == windowId && p.Urn == urn &&
                        (included ? IncludedPinclCodes.Contains(p.Pincl) : !IncludedPinclCodes.Contains(p.Pincl)));

    private async Task<(IReadOnlyList<PupilDto> Items, int TotalCount)> GetPageAsync(Guid windowId, string urn, bool included, string? search, int page, int pageSize)
    {
        var query = BaseQuery(windowId, urn, included);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => EF.Functions.ILike(p.Firstname, $"%{search}%") ||
                                     EF.Functions.ILike(p.Surname, $"%{search}%"));

        query = query.OrderBy(p => p.Surname).ThenBy(p => p.Firstname);

        var totalCount = await query.CountAsync();
        var items = await query.Skip(page * pageSize).Take(pageSize).Select(ToPupilDto).ToListAsync();

        return (items, totalCount);
    }

    private async Task<IReadOnlyList<PupilCsvDto>> GetAllAsync(Guid windowId, string urn, bool included)
        => await BaseQuery(windowId, urn, included)
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

    private async Task<IReadOnlyList<PupilSuggestionDto>> SearchAsync(Guid windowId, string urn, string query, bool included)
        => await BaseQuery(windowId, urn, included)
            .Where(p => EF.Functions.ILike(p.Surname, $"%{query}%") ||
                        EF.Functions.ILike(p.Firstname, $"%{query}%"))
            .OrderBy(p => p.Surname).ThenBy(p => p.Firstname)
            .Take(10)
            .Select(p => new PupilSuggestionDto(p.Id, $"{p.Surname}, {p.Firstname}, {p.DateOfBirth}"))
            .ToListAsync();

    private static System.Linq.Expressions.Expression<Func<Pupil, PupilDto>> ToPupilDto =>
        p => new PupilDto
        {
            Id = p.Id,
            Surname = p.Surname,
            Firstname = p.Firstname,
            Sex = p.Sex,
            DateOfBirth = p.DateOfBirth,
            Age = p.Age,
            FirstLanguage = p.FirstLanguage
        };
}
