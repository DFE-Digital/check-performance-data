using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;

namespace DfE.CheckPerformanceData.Application.ClaimsEnrichment;

public interface IClaimsEnrichmentService
{
    Task<ClaimsIdentity?> EnrichAsync(ClaimsPrincipal identity);
}
