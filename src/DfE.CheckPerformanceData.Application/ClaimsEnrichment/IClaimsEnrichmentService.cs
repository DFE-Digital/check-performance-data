using System.Security.Claims;

namespace DfE.CheckPerformanceData.Application.ClaimsEnrichment;

public interface IClaimsEnrichmentService
{
    Task<ClaimsIdentity?> EnrichAsync(ClaimsPrincipal identity);
}