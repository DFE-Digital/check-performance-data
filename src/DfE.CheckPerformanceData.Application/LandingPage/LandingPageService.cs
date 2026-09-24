using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
// Aliased, not imported: WindowManagement also declares a CheckingWindowDto.
using CheckingExerciseDto = DfE.CheckPerformanceData.Application.WindowManagement.CheckingExerciseDto;
using ICheckingDataReader = DfE.CheckPerformanceData.Application.WindowManagement.ICheckingDataReader;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public sealed class LandingPageService(ILandingPageRepository landingPageRepository, TimeProvider timeProvider,
    IDfESignInApiClient dfESignInApiClient, ICurrentUserService currentUserService,
    ICheckingDataReader checkingDataReader, ILogger<LandingPageService> logger) : ILandingPageService
{
    public async Task<LandingPageResult?> GetLandingPageDataAsync(CancellationToken cancellationToken)
    {
        // Guard: if the principal has no organisation_id claim, we have nothing to ask
        // DfE Sign-In about. Calling the API with an empty id 500s on the upstream side
        // and surfaces as an unhandled exception. Return null so the controller can
        // route to its existing no-data path (sign-out).
        if (string.IsNullOrWhiteSpace(currentUserService.OrganisationId))
        {
            return null;
        }

        OrganisationDto? organisation =
            await dfESignInApiClient.GetOrganisationAsync(currentUserService.UserId, currentUserService.OrganisationId);

        if (organisation == null)
        {
            return null;
        }
        
        var now = timeProvider.GetLocalNow();

        // TEMP DIAGNOSTIC (no-window-cards in preprod): record exactly what the clock
        // resolves to and the org context we filter against, so we can tell a clock/date
        // problem apart from a missing/mismatched-window one. Remove once diagnosed.
        logger.LogInformation(
            "Landing page diagnostics: GetLocalNow={LocalNow} (offset {Offset}, kind {Kind}), utcNow={UtcNow}, laestab {Laestab}, keyStages [{KeyStages}]",
            now, now.Offset, now.DateTime.Kind, timeProvider.GetUtcNow(),
            organisation.Laestab,
            string.Join(",", organisation.KeyStages.Select(ks => ks.KeyStage)));

        var openWindows = await landingPageRepository.GetOpenWindowsAsync(now.DateTime, organisation.Laestab, cancellationToken);

        // A window with no live exercise is not set up yet, so schools do not see it at all: no
        // card, and no "no data" or "not for your school" message that hints at work in progress.
        // "No data" is kept for a window that is ready but holds no file for this school.
        var windows = new List<CheckingWindowDto>(openWindows.Count);
        foreach (var window in openWindows)
        {
            var live = window.Exercises.Where(e => e.IsLiveAt(now.DateTime)).ToList();
            if (live.Count == 0) continue;

            window.HasPupilData = await HasDataInAnyAsync(window.Id, live, organisation.Laestab, cancellationToken);
            windows.Add(window);
        }

        var result = new LandingPageResult
        {
            OrganisationName = organisation.Name,
            OrganisationLaestab = organisation.Laestab,
            OrganisationUrn = organisation.Urn,
            KeyStages = organisation.KeyStages,
            OpenWindows = windows
                .Where(w => w.HasPupilData && organisation.KeyStages.Any(ks => ks.KeyStage == w.KeyStage)).ToList(),
            OrganisationAddress = organisation.Address
        };

        var notValidWindows = windows.Where(w => organisation.KeyStages.All(ks => ks.KeyStage != w.KeyStage)).ToList();
        result.NotValidWindows = notValidWindows;

        var noDataWindows = windows.Where(w => !w.HasPupilData && !notValidWindows.Contains(w)).ToList();
        result.NoDataWindows = noDataWindows;

        // TEMP DIAGNOSTIC (no-window-cards in preprod): the bucket split. All three at 0
        // means the repository returned no open windows for this laestab. Remove once diagnosed.
        logger.LogInformation(
            "Landing page diagnostics: openWindows={OpenCount}, cards={CardCount}, notValid={NotValidCount}, noData={NoDataCount} for laestab {Laestab}",
            windows.Count, result.OpenWindows.Count, notValidWindows.Count, noDataWindows.Count,
            organisation.Laestab);

        return result;
    }

    private async Task<bool> HasDataInAnyAsync(Guid windowId, IEnumerable<CheckingExerciseDto> exercises,
        string laestab, CancellationToken cancellationToken)
    {
        foreach (var exercise in exercises)
            if (await checkingDataReader.HasSchoolDataAsync(windowId, exercise, laestab, cancellationToken))
                return true;
        return false;
    }
}