using System.Security.Claims;
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
        var userid = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        var orgJson = identity.FindFirst("organisation")?.Value ?? "{}";
        var org = JsonNode.Parse(orgJson);
        var orgId = org["id"]?.ToString() ?? string.Empty;
        
        var organisation = await apiClient.GetOrganisationAsync(userid, orgId);

        identity.AddClaim(new Claim("low_age", organisation.StatutoryLowAge.ToString()));
        identity.AddClaim(new Claim("high_age", organisation.StatutoryHighAge.ToString()));
    }
}