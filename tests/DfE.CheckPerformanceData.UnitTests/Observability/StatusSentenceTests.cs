using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Application.UnitTests.Observability;

// The plain-English status sentence narrates the health strip. Its leading clause is one of
// the exact UI-SPEC strings per health state; the figures (processed today, typical
// end-to-end) are appended dynamically. The exact leading-clause copy is pinned here so a
// reword is a deliberate, test-visible change.
public sealed class StatusSentenceTests
{
    private readonly StatusSentenceBuilder _sut = new();

    [Fact]
    public void Build_Green_StartsWithAllSystemsHealthy()
    {
        var sentence = _sut.Build(HealthLevel.Flowing, processedToday: 42, typicalEndToEnd: TimeSpan.FromSeconds(8));

        Assert.StartsWith("All systems healthy.", sentence);
    }

    [Fact]
    public void Build_Amber_UsesBackingUpCopy()
    {
        var sentence = _sut.Build(HealthLevel.BackingUp, processedToday: 10, typicalEndToEnd: TimeSpan.FromSeconds(20));

        Assert.StartsWith(
            "One or more queues are backing up. Throughput is slower than usual but nothing has failed.",
            sentence);
    }

    [Fact]
    public void Build_Red_UsesNeedsAttentionCopy()
    {
        var sentence = _sut.Build(HealthLevel.NeedsAttention, processedToday: 3, typicalEndToEnd: TimeSpan.FromSeconds(40));

        Assert.StartsWith(
            "Something needs attention: the dead-letter queue is rising or a queue has stalled. Open the queues page to investigate.",
            sentence);
    }

    [Fact]
    public void Build_IncludesTheProcessedTodayFigure()
    {
        var sentence = _sut.Build(HealthLevel.Flowing, processedToday: 42, typicalEndToEnd: TimeSpan.FromSeconds(8));

        Assert.Contains("42", sentence);
    }
}
