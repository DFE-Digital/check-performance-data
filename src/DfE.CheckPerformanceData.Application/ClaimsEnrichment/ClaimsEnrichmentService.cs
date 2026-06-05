using System.Security.Claims;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.ClaimsEnrichment;

public sealed class ClaimsEnrichmentService(IDfESignInApiClient apiClient) : IClaimsEnrichmentService
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

        var organisation = await apiClient.GetOrganisationAsync(userid, orgId);
        
        var newIdentity = new ClaimsIdentity(roleClaims, "DfeSignIn");
        newIdentity.AddClaim(new Claim("organisation_id", orgId, ClaimValueTypes.String));
        newIdentity.AddClaim(new Claim("organisation_name", organisation!.Name, ClaimValueTypes.String));
        newIdentity.AddClaim(new Claim("organisation_urn", organisation.Urn, ClaimValueTypes.String));
        if (organisation.Type?.Id is { } typeId)
            newIdentity.AddClaim(new Claim("organisation_type_id", typeId, ClaimValueTypes.String));
        return newIdentity;
    }
}