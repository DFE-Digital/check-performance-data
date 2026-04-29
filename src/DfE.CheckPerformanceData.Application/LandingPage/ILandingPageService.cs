using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public interface ILandingPageService
{
    Task<LandingPageResult?> GetLandingPageDataAsync(CancellationToken cancellationToken);
}

public class LandingPageResult
{
    public required string OrganisationName { get; set; }
    public required string OrganisationLaestab { get; set; }
    public required string OrganisationUrn { get; set; }
    public required List<OrganisationKeyStageDto> KeyStages { get; set; }
    public required List<OpenCheckingWindowDto> OpenWindows { get; set; }
    public string OrganisationAddress { get; set; } = string.Empty;
}

public class OpenCheckingWindowDto
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public required DateTime EndDate { get; init; }
    public required KeyStages KeyStage { get; init; }
}

