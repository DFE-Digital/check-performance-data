using DfE.CheckPerformanceData.Application.ClaimsEnrichment;

namespace DfE.CheckPerformanceData.Application.UnitTests.ClaimsEnrichment;

public sealed class ClaimsEnrichmentServiceTests
{
    [Fact]
    public void ClaimsEnrichmentService_IsSealed()
    {
        Assert.True(
            typeof(ClaimsEnrichmentService).IsSealed,
            "ClaimsEnrichmentService must be sealed per project convention (CONVENTIONS.md).");
    }
}
