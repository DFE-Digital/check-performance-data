using System.Security.Claims;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.ClaimsEnrichment;

public class ClaimsEnrichmentService: IClaimsEnrichmentService
{
    private readonly IDfESignInApiClient _apiClient;

    public ClaimsEnrichmentService(IDfESignInApiClient apiClient)
    {
        _apiClient = apiClient;
    }
    public async Task EnrichAsync(ClaimsIdentity identity)
    {
        var userid = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        var orgJson = identity.FindFirst("organisation")?.Value ?? "{}";
        var org = JsonNode.Parse(orgJson);
        var orgId = org["id"]?.ToString() ?? string.Empty;
        
        var organisation = await _apiClient.GetOrganisationAsync(userid, orgId);

        identity.AddClaim(new Claim("low_age", organisation.StatutoryLowAge.ToString()));
        identity.AddClaim(new Claim("high_age", organisation.StatutoryHighAge.ToString()));
        identity.AddClaim(new Claim("organisation_id", organisation.Id));
        identity.AddClaim(new Claim("organisation_laestab", organisation.LAESTAB));
        identity.AddClaim(new Claim("organisation_name", organisation.Name));
        identity.AddClaim(new Claim("organisation_urn", organisation.Urn));
    }
}