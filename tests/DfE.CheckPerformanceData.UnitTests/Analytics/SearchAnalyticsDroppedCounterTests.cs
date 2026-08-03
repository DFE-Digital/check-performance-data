using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.UnitTests.Analytics;

// Contract tests for SearchAnalyticsDroppedCounter — the in-process singleton counter that
// SinkAndLogSearchTelemetry increments when the bounded analytics channel refuses a write
// (Channel.Writer.TryWrite returned false). Mirrors SearchZeroResultsCounter exactly: the
// counter is not persisted, is not exported to any external metrics backend, and does not
// survive a process restart — it exists so diagnostic surfaces + tests can see whether
// events were shed under load. Three facts: fresh read is zero, single Increment reads one,
// and 10 000 concurrent Increments through Parallel.For read exactly 10 000 (the
// thread-safety pin — a plain long ++ would drift).
public sealed class SearchAnalyticsDroppedCounterTests
{
    [Fact]
    public void Read_OnFreshCounter_ReturnsZero()
    {
        var sut = new SearchAnalyticsDroppedCounter();

        Assert.Equal(0L, sut.Read());
    }

    [Fact]
    public void Increment_SingleCall_ReadReturnsOne()
    {
        var sut = new SearchAnalyticsDroppedCounter();

        sut.Increment();

        Assert.Equal(1L, sut.Read());
    }

    [Fact]
    public void Increment_TenThousandParallelCalls_ReadReturnsExactlyTenThousand()
    {
        var sut = new SearchAnalyticsDroppedCounter();

        Parallel.For(0, 10_000, _ => sut.Increment());

        Assert.Equal(10_000L, sut.Read());
    }

    // Read must be idempotent — inspecting the counter must not disturb it. Two reads
    // straddling nothing must return the same value.
    [Fact]
    public void Read_IsIdempotent_DoesNotChangeValue()
    {
        var sut = new SearchAnalyticsDroppedCounter();
        sut.Increment();
        sut.Increment();

        var first = sut.Read();
        var second = sut.Read();

        Assert.Equal(2L, first);
        Assert.Equal(2L, second);
    }
}
