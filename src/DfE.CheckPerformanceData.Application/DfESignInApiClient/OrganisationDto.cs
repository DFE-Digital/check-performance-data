using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.DfESignInApiClient;

public class OrganisationDto
{
    public required string Id { get; init; }
    public string Name { get; init; } = string.Empty;
    [JsonIgnore]
    public required string Laestab { get; set; }
    public required string Urn { get; init; }
    public int StatutoryLowAge { get; init; }
    public int StatutoryHighAge { get; init; }
    
    public List<OrganisationKeyStageDto> KeyStages =>
        OrganisationKeyStages.All
                .Where(ks => StatutoryLowAge  < ks.HighAge
                             && StatutoryHighAge > ks.LowAge)
                .ToList();
}