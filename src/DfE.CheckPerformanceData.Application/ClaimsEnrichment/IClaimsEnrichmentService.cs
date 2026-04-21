using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.ClaimsEnrichment;

public interface IClaimsEnrichmentService
{
    Task EnrichAsync(ClaimsIdentity identity);
}

public class ClaimsEnrichmentService(IDfESignInApiClient apiClient) : IClaimsEnrichmentService
{
    public async Task EnrichAsync(ClaimsIdentity identity)
    {
        string userid = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new Exception ("userId not found");
        
        string orgJson = identity.FindFirst("organisation")?.Value ?? throw new Exception ("Organisation not found");
        JsonNode? org = null;
        try
        {
            org = JsonNode.Parse(orgJson);
        }
        catch (JsonException je)
        {
            throw new JsonException($"Invalid JSON: {je.Message}");
        }

        string orgId = org["id"]?.ToString() ?? throw new Exception("Organisation id not found");
        
        OrganisationDto organisation = await apiClient.GetOrganisationAsync(userid, orgId) ?? throw new Exception("Organisation not found");

        if (organisation.StatutoryLowAge is not null && organisation.StatutoryHighAge is not null)
        {

            identity.AddClaim(new Claim("low_age", organisation.StatutoryLowAge.ToString()));
            identity.AddClaim(new Claim("high_age", organisation.StatutoryHighAge.ToString()));
        }
        else throw new Exception("Statutory age range not found");
    }
}