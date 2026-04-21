using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

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
        
        var now = timeProvider.GetUtcNow();
        var windows = await landingPageRepository.GetOpenWindowsAsync(DateOnly.FromDateTime(now.DateTime),
            organisation.KeyStages.Select(k => k.KeyStage), cancellationToken);

        var result = new LandingPageResult
        {
            OrganisationName = organisation.Name,
            OrganisationLaestab = organisation.Laestab,
            OrganisationUrn = organisation.Urn,
            KeyStages = organisation.KeyStages,
            OpenWindows = windows,
            OrganisationAddress =  organisation.Address
        };
        
        return result;
    }
}