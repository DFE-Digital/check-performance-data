using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.DfESignInApiClient;

public class OrganisationDto
{
    public required string Id { get; init; }
    public string Name { get; init; } = string.Empty;
    [JsonIgnore]
    public string Laestab { get; set; } = null!;

    public required string Urn { get; init; }
    public int? StatutoryLowAge { get; init; }
    public int? StatutoryHighAge { get; init; }

    public string Address { get; init; } = string.Empty;
    
    public List<OrganisationKeyStageDto> KeyStages =>
        OrganisationKeyStages.All
            .Where(ks => StatutoryLowAge  < ks.HighAge
                         && StatutoryHighAge > ks.LowAge)
            .ToList();
}

public static class OrganisationKeyStages
{
    public static readonly OrganisationKeyStageDto KS2 = new(1, "Key Stage 2", 3, 11, KeyStages.KS2);
    public static readonly OrganisationKeyStageDto KS4 = new(2, "Key Stage 4", 14, 16, KeyStages.KS4);
    public static readonly OrganisationKeyStageDto SixteenToEighteen = new(3, "16 to 18", 16, 18, KeyStages.Post16);

    public static readonly IReadOnlyList<OrganisationKeyStageDto> All = [KS2, KS4, SixteenToEighteen];
}

public record OrganisationKeyStageDto(int Id, string Title, int LowAge, int HighAge, KeyStages KeyStage);

//
// [ {
//     "id" : "09158CF5-A701-47E8-BDCD-4EA201B024A3",
//     "name" : "Department for Education",
//     "LegalName" : null,
//     "category" : {
//         "id" : "002",
//         "name" : "Local Authority"
//     },
//     "urn" : null,
//     "uid" : null,
//     "upin" : null,
//     "ukprn" : null,
//     "establishmentNumber" : "001",
//     "status" : {
//         "id" : 1,
//         "name" : "Open",
//         "tagColor" : "green"
//     },
//     "closedOn" : null,
//     "address" : null,
//     "telephone" : null,
//     "statutoryLowAge" : null,
//     "statutoryHighAge" : null,
//     "legacyId" : "1031237",
//     "companyRegistrationNumber" : null,
//     "SourceSystem" : null,
//     "providerTypeName" : null,
//     "ProviderTypeCode" : null,
//     "GIASProviderType" : null,
//     "PIMSProviderType" : null,
//     "PIMSProviderTypeCode" : null,
//     "PIMSStatusName" : null,
//     "pimsStatus" : null,
//     "GIASStatusName" : null,
//     "GIASStatus" : null,
//     "MasterProviderStatusName" : null,
//     "MasterProviderStatusCode" : null,
//     "OpenedOn" : null,
//     "DistrictAdministrativeName" : null,
//     "DistrictAdministrativeCode" : null,
//     "DistrictAdministrative_code" : null,
//     "IsOnAPAR" : null
// } ]
