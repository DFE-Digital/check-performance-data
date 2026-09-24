using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto, which would make the
// LandingPage one ambiguous here.
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using CheckingExerciseReleaseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseReleaseDto;
using CheckingExerciseReleaseFileDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseReleaseFileDto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class LandingPageRepository(
    IPortalDbContext dbContext,
    ILogger<LandingPageRepository> logger) : ILandingPageRepository
{
    public async Task<List<CheckingWindowDto>> GetOpenWindowsAsync(DateTime now, string laestab,
        CancellationToken cancellationToken)
    {
        // TEMP DIAGNOSTIC (no-window-cards in preprod): fetch EVERY window (unfiltered) and
        // log its date range against `now`, so we can see whether an existing window simply
        // isn't bracketing the clock value. Windows are few, so scanning all is cheap.
        // Filtering happens in memory below. Revert to the DB-side date Where() once diagnosed.
        var allWindows = await dbContext.CheckingWindows
            .AsNoTracking()
            .Select(w => new
            {
                w.StartDate,
                w.EndDate,
                w.KeyStage,
                w.CheckingWindowType,
                w.Title,
                w.TurnaroundCommitment,
                w.NextOpportunity,
                w.Id,
                // #315: the landing page cannot tell whether a Post16 window's results enquiry is
                // running from the window's own dates — only the exercise rows say that.
                Exercises = w.CheckingExercises
                    .OrderBy(e => e.SortOrder)
                    .Select(e => new CheckingExerciseDto
                    {
                        Id = e.Id,
                        ExerciseType = e.ExerciseType,
                        StartDate = e.StartDate,
                        EndDate = e.EndDate,
                        SortOrder = e.SortOrder,
                        // The service decides from these whether the window is shown at all, and
                        // where to look for the school's files.
                        TabName = e.TabName,
                        IsEnabled = e.IsEnabled,
                        VisibleFrom = e.VisibleFrom,
                        VisibleUntil = e.VisibleUntil,
                        UsesExerciseStorage = e.UsesExerciseStorage,
                        CurrentReleaseId = e.CurrentReleaseId,
                        // Only the current release: its files name the per-dataset outputs.
                        Releases = e.Releases
                            .Where(r => r.Id == e.CurrentReleaseId)
                            .Select(r => new CheckingExerciseReleaseDto
                            {
                                Id = r.Id,
                                Number = r.Number,
                                Files = r.Files
                                    .Select(f => new CheckingExerciseReleaseFileDto
                                    {
                                        DatasetId = f.DatasetId,
                                        DatasetName = f.DatasetName
                                    })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "Landing page window scan: now={Now} (kind {Kind}), totalWindowsInDb={Total}",
            now, now.Kind, allWindows.Count);

        foreach (var w in allWindows)
        {
            logger.LogInformation(
                "Window '{Title}' ({Id}): start={Start}, end={End}, keyStage={KeyStage}, isOpen={IsOpen}",
                w.Title, w.Id, w.StartDate, w.EndDate, w.KeyStage, w.StartDate <= now && w.EndDate >= now);
        }

        var windows = allWindows.Where(w => w.StartDate <= now && w.EndDate >= now).ToList();

        var result = new List<CheckingWindowDto>(windows.Count);
        foreach (var w in windows)
        {
            result.Add(new CheckingWindowDto
            {
                StartDate = w.StartDate,
                EndDate = w.EndDate,
                KeyStage = w.KeyStage,
                CheckingWindowType = w.CheckingWindowType,
                Title = w.Title,
                TurnaroundCommitment = w.TurnaroundCommitment,
                NextOpportunity = w.NextOpportunity,
                Id = w.Id,
                Exercises = w.Exercises
            });
        }

        return result;
    }
}
