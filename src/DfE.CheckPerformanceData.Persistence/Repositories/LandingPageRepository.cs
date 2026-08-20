using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.LandingPage;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto, which would make the
// LandingPage one ambiguous here.
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class LandingPageRepository(
    IPortalDbContext dbContext,
    IPupilDataBlobClient pupilDataBlobClient,
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
                        SortOrder = e.SortOrder
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
                Id = w.Id,
                // #316: "has pupil data" asks about the pupil-data exercise's prefix specifically.
                HasPupilData = await pupilDataBlobClient.HasPupilDataAsync(
                    w.Id, CheckingExerciseType.PupilData, laestab),
                Exercises = w.Exercises
            });
        }

        return result;
    }
}
