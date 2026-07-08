using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public sealed class LandingPageService(ILandingPageRepository landingPageRepository, TimeProvider timeProvider,
    IDfESignInApiClient dfESignInApiClient, ICurrentUserService currentUserService,
    ILogger<LandingPageService> logger) : ILandingPageService
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

        var organisation =
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

        var windows = await landingPageRepository.GetOpenWindowsAsync(now.DateTime, organisation.Laestab, cancellationToken);

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
        if (notValidWindows.Count > 0)
        {
            result.NotValidWindowsText = string.Join(',', notValidWindows.Select(w => w.Title));
        }

        var noDataWindows = windows.Where(w => !w.HasPupilData && !notValidWindows.Contains(w)).ToList();
        if (noDataWindows.Count > 0)
        {
            result.NoDataWindowsText = string.Join(',', noDataWindows.Select(w => w.Title));
        }

        // TEMP DIAGNOSTIC (no-window-cards in preprod): the bucket split. All three at 0
        // means the repository returned no open windows for this laestab. Remove once diagnosed.
        logger.LogInformation(
            "Landing page diagnostics: openWindows={OpenCount}, cards={CardCount}, notValid={NotValidCount}, noData={NoDataCount} for laestab {Laestab}",
            windows.Count, result.OpenWindows.Count, notValidWindows.Count, noDataWindows.Count,
            organisation.Laestab);

        return result;
    }
}