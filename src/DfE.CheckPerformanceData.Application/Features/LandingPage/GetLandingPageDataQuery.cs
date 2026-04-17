using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using MediatR;

namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

public record GetLandingPageDataQuery : IRequest<LandingPageResult?>;

public class LandingPageResult
{
    public required string OrganisationName { get; set; }
    public required string OrganisationLaestab { get; set; }
    public required string OrganisationUrn { get; set; }
    public required List<OrganisationKeyStageDto> KeyStages { get; set; }
    public required List<OpenCheckingWindowDto> OpenWindows { get; set; }
}