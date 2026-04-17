using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using MediatR;

namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

public class GetLandingPageDataQueryHandler(ILandingPageRepository landingPageRepository, TimeProvider timeProvider, 
    IDfESignInApiClient dfESignInApiClient, ICurrentUserService currentUserService) 
    : IRequestHandler<GetLandingPageDataQuery, LandingPageResult>
{
    public async Task<LandingPageResult> Handle(GetLandingPageDataQuery request, CancellationToken cancellationToken)
    {
        var organisation =
            await dfESignInApiClient.GetOrganisationAsync(currentUserService.UserId, currentUserService.OrganisationId);
        
        var now = timeProvider.GetUtcNow();
        var windows = await landingPageRepository.GetOpenWindowsAsync(now,
            organisation.KeyStages.Select(k => k.KeyStage), cancellationToken);

        var result = new LandingPageResult
        {
            OrganisationName = organisation.Name,
            OrganisationLaestab = organisation.LAESTAB,
            OrganisationUrn = organisation.Urn,
            KeyStages = organisation.KeyStages,
            OpenWindows = windows
        };
        
        return result;
    }
}