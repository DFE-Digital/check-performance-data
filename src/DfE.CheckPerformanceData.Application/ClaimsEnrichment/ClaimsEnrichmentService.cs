using System.Security.Claims;
using System.Text.Json.Nodes;

namespace DfE.CheckPerformanceData.Application.ClaimsEnrichment;

public class ClaimsEnrichmentService : IClaimsEnrichmentService
{
    public void EnrichAsync(ClaimsIdentity identity)
    {
        var orgJson = identity.FindFirst("organisation")?.Value ?? "{}";
        var org = JsonNode.Parse(orgJson);
        if (org == null) return;

        var orgId = org["id"]?.ToString() ?? string.Empty;
        identity.AddClaim(new Claim("organisation_id", orgId, ClaimValueTypes.String));
    }
}