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
    public required List<CheckingWindowDto> OpenWindows { get; set; }
    public string? NotValidWindowsText { get; set; }
    public string? NoDataWindowsText { get; set; }
    public string OrganisationAddress { get; set; } = string.Empty;
    // public required List<CheckingWindowDto> ClosedWindows { get; set; }
}

public class CheckingWindowDto
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public required DateTime EndDate { get; init; }
    public required KeyStages KeyStage { get; init; }
    public bool HasPupilData { get; init; }
    public required DateTime StartDate { get; init; }
}

