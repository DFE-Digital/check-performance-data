using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using MediatR;

namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

public record GetLandingPageDataQuery : IRequest<LandingPageResult>;

public class LandingPageResult
{
    public string OrganisationName { get; set; }
    public string OrganisationLaestab { get; set; }
    public string OrganisationUrn { get; set; }
    public List<OrganisationKeyStageDto> KeyStages { get; set; }
    public List<OpenCheckingWindowDto> OpenWindows { get; set; }
}