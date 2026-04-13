using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

public class GetLandingPageDataQueryHandler(IPortalDbContext dbContext, TimeProvider timeProvider, IDfESignInApiClient dfESignInApiClient) 
    : IRequestHandler<GetLandingPageDataQuery, LandingPageResult>
{
    public async Task<LandingPageResult> Handle(GetLandingPageDataQuery request, CancellationToken cancellationToken)
    {
        var organisation =
            await dfESignInApiClient.GetOrganisationAsync(request.UserId, request.OrganisationId);
        
        var now = timeProvider.GetUtcNow();
        var windows = await dbContext.CheckingWindows
            .Where(window 
                => window.StartDate <= now 
                   && window.EndDate >= now 
                   && organisation.KeyStages.Select(k => k.KeyStage).Contains(window.KeyStage)
            )
            .ToListAsync(cancellationToken);

        var result = new LandingPageResult
        {
            OrganisationName = organisation.Name,
            OrganisationLaestab = organisation.LAESTAB,
            OrganisationUrn = organisation.Urn,
            KeyStages = organisation.KeyStages,
            OpenWindows = windows.Select(w => new OpenWindowDto()
                { EndDate = w.EndDate, KeyStage = w.KeyStage, Title = w.Title }).ToList()
        };
        
        return result;
    }
}