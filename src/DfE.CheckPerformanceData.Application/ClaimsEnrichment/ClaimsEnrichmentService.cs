using System.Security.Claims;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.ClaimsEnrichment;

public class ClaimsEnrichmentService(IDfESignInApiClient apiClient) : IClaimsEnrichmentService
{
    public async Task<ClaimsIdentity?> EnrichAsync(ClaimsPrincipal identity)
    {
        var userid = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userid == null) return null;
        
        var orgJson = identity.FindFirst("organisation")?.Value;
        if (orgJson == null) return null;
        
        var org = JsonNode.Parse(orgJson);
        var orgId = org!["id"]?.ToString() ?? string.Empty;
        
        var roles = await apiClient.GetUserRolesAsync(orgId, userid);
        if (roles.Count == 0) return null;
        
        var roleClaims = roles.Select(r => new Claim(ClaimTypes.Role, r.Code));
        
        var newIdentity = new ClaimsIdentity(roleClaims, "DfeSignIn");
        newIdentity.AddClaim(new Claim("organisation_id", orgId, ClaimValueTypes.String));
        return newIdentity;
    }
}