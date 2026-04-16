using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.DfESignInApiClient;

public class OrganisationDto
{
    public string Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? LegalName { get; init; }
    
    public string? Urn { get; init; }
    public string? Uid { get; init; }
    public string? Upin { get; init; }
    public string? Ukprn { get; init; }
    public string? EstablishmentNumber { get; init; }
    
    public string? Address { get; init; }
    public string? Telephone { get; init; }
    public int? StatutoryLowAge { get; init; }
    public int? StatutoryHighAge { get; init; }
    public string? LegacyId { get; init; }
    public string? CompanyRegistrationNumber { get; init; }
    
    [JsonIgnore]
    public string? LAESTAB { get; set; }
}


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
