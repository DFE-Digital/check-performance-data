using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public sealed class LandingPageService(ILandingPageRepository landingPageRepository, TimeProvider timeProvider, 
    IDfESignInApiClient dfESignInApiClient, ICurrentUserService currentUserService) : ILandingPageService
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

        return result;
    }
}