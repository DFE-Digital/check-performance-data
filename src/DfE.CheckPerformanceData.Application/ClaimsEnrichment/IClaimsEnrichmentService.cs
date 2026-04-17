using System.Security.Claims;

namespace DfE.CheckPerformanceData.Application.ClaimsEnrichment;

public interface IClaimsEnrichmentService
{
    void EnrichAsync(ClaimsIdentity identity);
}