using System.Globalization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto, which would make the
// LandingPage one ambiguous here.
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using CheckingWindowDatasetDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingWindowDatasetDto;
using CheckingExerciseReleaseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseReleaseDto;
using CheckingExerciseReleaseFileDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseReleaseFileDto;
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

    public async Task<IReadOnlyList<IPupilRecord>> GetAllPupilsForSchoolAsync(Guid windowId, string laestab)
        => await GetSchoolPupilsAsync(windowId, laestab);

    public async Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId)
        => await dbContext.CheckingWindows
            .AsNoTracking()
            // Datasets and Releases are sibling collections. In one query each release file would
            // repeat every dataset row; split queries load each collection once.
            .AsSplitQuery()
            .Where(w => w.Id == windowId)
            .Select(w => new CheckingWindowDto
            {
                Id = w.Id,
                EndDate = w.EndDate,
                Title = w.Title,
                KeyStage = w.KeyStage,
                CheckingWindowType = w.CheckingWindowType,
                StartDate = w.StartDate,
                TurnaroundCommitment = w.TurnaroundCommitment,
                NextOpportunity = w.NextOpportunity,
                // #315: ICheckingExerciseService answers "is this exercise open" from these rows,
                // so every read path that reaches Web has to carry them. #466: the exercise tab
                // builder needs the rest — TabName is what decides whether a row draws a tab at
                // all, and Datasets is what lets it read the schema that shapes one. Mirrors the
                // projection WindowRepository already carries for the admin summary page.
                Exercises = w.CheckingExercises
                    .OrderBy(e => e.SortOrder)
                    .Select(e => new CheckingExerciseDto
                    {
                        Id = e.Id,
                        UsesExerciseStorage = e.UsesExerciseStorage,
                        Name = e.Name,
                        TabName = e.TabName,
                        TabOrder = e.TabOrder,
                        IsEnabled = e.IsEnabled,
                        DisplayOnly = e.DisplayOnly,
                        VisibleFrom = e.VisibleFrom,
                        VisibleUntil = e.VisibleUntil,
                        WindowStart = w.StartDate,
                        WindowEnd = w.EndDate,
                        ReplacesCheckingExerciseId = e.ReplacesCheckingExerciseId,
                        CurrentReleaseId = e.CurrentReleaseId,
                        Layout = e.Layout,
                        // Oldest first. The school-facing display reads its schemas from the current release, and
                        // the admin exercise page lists every release so an earlier one can be made live again.
                        Releases = e.Releases
                            .OrderBy(r => r.Number)
                            .Select(r => new CheckingExerciseReleaseDto
                            {
                                Id = r.Id,
                                Number = r.Number,
                                PublishedAt = r.PublishedAt,
                                PublishedBy = r.PublishedBy,
                                FilesWritten = r.FilesWritten,
                                Files = r.Files
                                    .OrderBy(f => f.SortOrder)
                                    .Select(f => new CheckingExerciseReleaseFileDto
                                    {
                                        DatasetId = f.DatasetId,
                                        DatasetName = f.DatasetName,
                                        FeedsJourney = f.FeedsJourney,
                                        Included = f.Included,
                                        SourceFile = f.SourceFile,
                                        IngressFile = f.IngressFile,
                                        IngressFileChecksum = f.IngressFileChecksum,
                                        SchemaFile = f.SchemaFile,
                                        SchemaFileChecksum = f.SchemaFileChecksum,
                                        SortOrder = f.SortOrder
                                    })
                                    .ToList()
                            })
                            .ToList(),
                        ExerciseType = e.ExerciseType,
                        StartDate = e.StartDate,
                        EndDate = e.EndDate,
                        SortOrder = e.SortOrder,
                        Datasets = e.Datasets
                            .OrderBy(d => d.SortOrder)
                            .Select(d => new CheckingWindowDatasetDto
                            {
                                Id = d.Id,
                                Name = d.Name,
                                IngressFile = d.IngressFile,
                                IngressFileChecksum = d.IngressFileChecksum,
                                SchemaFile = d.SchemaFile,
                                SchemaFileChecksum = d.SchemaFileChecksum,
                                Included = d.Included,
                                SourceFile = d.SourceFile,
                                Required = d.Required,
                                FeedsJourney = d.FeedsJourney,
                                SortOrder = d.SortOrder
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .SingleAsync();

    public async Task<PupilDto> GetPupilAsync(Guid windowId, string laestab, Guid pupilId)
    {
        var pupils = await GetSchoolPupilsAsync(windowId, laestab);
        return ToPupilDto(pupils.Single(p => p.Id == pupilId));
    }

    public async Task<IReadOnlyList<PupilSuggestionDto>> SearchPupilsAsync(Guid windowId, string laestab, string urn, string query, PupilFilter filter, Guid? excludeId = null, IReadOnlySet<string>? cypmdIdAllowList = null)
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

        // Applied here rather than after the cap below: ten pupils who hold no results would
        // otherwise crowd out the one who does. The set carries its own comparer (the results
        // client builds it case-insensitively), so Contains is asked, never a re-implementation.
        if (cypmdIdAllowList is not null)
            pupils = pupils.Where(p => cypmdIdAllowList.Contains(p.Cypmd_Id));

        return pupils
            .OrderBy(p => p.Surname).ThenBy(p => p.Firstname)
            .Take(10)
            .Select(p => new PupilSuggestionDto(
                p.Id,
                PupilSuggestionFormat.Label(p, windowType),
                p.Firstname,
                p.Surname,
                PupilDateFormatter.ToDisplayDate(p.DateOfBirth)))
            .ToList();
    }

    private sealed record SchoolPupilsCacheEntry(IReadOnlyList<IPupilRecord> Pupils, CheckingWindowType WindowType);

    private async Task<IReadOnlyList<IPupilRecord>> GetSchoolPupilsAsync(Guid windowId, string laestab)
        => (await GetSchoolPupilsWithWindowTypeAsync(windowId, laestab)).Pupils;

    private async Task<SchoolPupilsCacheEntry> GetSchoolPupilsWithWindowTypeAsync(Guid windowId, string laestab)
    {
        // The current release is part of the key. When an admin publishes a new release, or makes
        // an earlier one live again, the next read misses and loads that release's file, rather
        // than serving the old one until the 30-minute sliding expiry runs out.
        var releases = await dbContext.CheckingExercises
            .AsNoTracking()
            .Where(e => e.CheckingWindowId == windowId && e.ExerciseType == CheckingExerciseType.PupilData)
            .OrderBy(e => e.Id)
            .Select(e => e.CurrentReleaseId)
            .ToListAsync();
        var key = $"pupils:{windowId}:{laestab}:{string.Join(",", releases)}";
        if (cache.TryGetValue(key, out SchoolPupilsCacheEntry? cached) && cached is not null)
            return cached;

        // The blob's record shape depends on the window type, so the window is resolved first.
        var window = await GetCheckingWindowAsync(windowId);
        var pupils = await pupilDataBlobClient.GetPupilsAsync(
            windowId, CheckingExerciseType.PupilData, laestab, window.CheckingWindowType) ?? [];
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
