using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public class LandingPageService(ILandingPageRepository landingPageRepository, TimeProvider timeProvider, 
    IDfESignInApiClient dfESignInApiClient, ICurrentUserService currentUserService) : ILandingPageService
{
    public async Task<LandingPageResult?> GetLandingPageDataAsync(CancellationToken cancellationToken)
    {
        var organisation =
            await dfESignInApiClient.GetOrganisationAsync(currentUserService.UserId, currentUserService.OrganisationId);

        if (organisation == null)
        {
            return null;
        }
        
        var now = timeProvider.GetLocalNow();
        var windows = await landingPageRepository.GetOpenWindowsAsync(now.DateTime, organisation.Urn, cancellationToken);
        
        // var closedWindows = await landingPageRepository.GetClosedWindowsAsync(now.DateTime,
        //     organisation.KeyStages.Select(k => k.KeyStage), organisation.Laestab, cancellationToken);

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