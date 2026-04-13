using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.ClaimsEnrichment;

public class ClaimsEnrichmentService(IDfESignInApiClient apiClient) : IClaimsEnrichmentService
{
    public async Task EnrichAsync(ClaimsIdentity identity)
    {
        var userid = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        var orgJson = identity.FindFirst("organisation")?.Value ?? "{}";
        var org = JsonNode.Parse(orgJson);
        var orgId = org["id"]?.ToString() ?? string.Empty;
        
        identity.AddClaim(new Claim("organisation_id", orgId, ClaimValueTypes.String));
        
        // var organisation = await apiClient.GetOrganisationAsync(userid, orgId);
        //
        // identity.AddClaim(new Claim("organisation_low_age", organisation.StatutoryLowAge.ToString()));
        // identity.AddClaim(new Claim("organisation_high_age", organisation.StatutoryHighAge.ToString()));
        // identity.AddClaim(new Claim("organisation_name", organisation.Name));
        // identity.AddClaim(new Claim("organisation_urn", organisation.Urn));
        // identity.AddClaim(new Claim("organisation_laestab", organisation.LAESTAB));
        // identity.AddClaim(new Claim("organisation_keystages", JsonSerializer.Serialize(organisation.KeyStages)));
    }
}