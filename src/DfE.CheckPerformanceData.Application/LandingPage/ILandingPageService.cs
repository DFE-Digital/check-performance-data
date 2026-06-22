using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public interface ILandingPageService
{
    Task<LandingPageResult?> GetLandingPageDataAsync(CancellationToken cancellationToken);
}

public sealed class LandingPageResult
{
    public required string OrganisationName { get; set; }
    public required string OrganisationLaestab { get; set; }
    public required string OrganisationUrn { get; set; }
    public required List<OrganisationKeyStageDto> KeyStages { get; set; }
    public required List<CheckingWindowDto> OpenWindows { get; set; }
    public string? NotValidWindowsText { get; set; }
    public string? NoDataWindowsText { get; set; }
    public string OrganisationAddress { get; set; } = string.Empty;
}

public sealed class CheckingWindowDto
{
    public Guid Id { get; init; }
    public required string Title { get; set; }
    public required DateTime EndDate { get; set; }
    public required KeyStages KeyStage { get; set; }
    public required CheckingWindowType CheckingWindowType { get; set; }
    public bool HasPupilData { get; init; }
    public required DateTime StartDate { get; set; }
    public string IngressFile { get; set; } = string.Empty;
    public string IngressFileChecksum { get; set; } = string.Empty;
    public string SchemaFile { get; set; } = string.Empty;
    public string SchemaFileChecksum { get; set; } = string.Empty;
}

