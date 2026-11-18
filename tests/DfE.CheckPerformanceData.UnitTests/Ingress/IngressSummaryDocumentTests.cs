using DfE.CheckPerformanceData.Infrastructure.Ingress;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

public sealed class IngressSummaryDocumentTests
{
    [Fact]
    public void ADocumentWithNoOutcomeSet_HasTheFailedOutcome() =>
        Assert.Equal("failed", new IngressSummaryDocument().Outcome);
}
