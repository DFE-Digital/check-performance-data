using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

// Contract tests for SearchZeroResultsCounter — the in-process singleton counter that
// LoggerSearchTelemetry increments when a search request produces an empty hit set. Three
// facts: fresh-read is zero, single Increment reads one, and 10 000 concurrent Increments
// through Parallel.For read exactly 10 000. The parallel fact is the thread-safety pin
// documented in the Search research pack — the Interlocked-backed implementation must
// survive it; a plain long ++ would drift.
public sealed class SearchZeroResultsCounterTests
{
    [Fact]
    public void Read_OnFreshCounter_ReturnsZero()
    {
        var sut = new SearchZeroResultsCounter();

        Assert.Equal(0L, sut.Read());
    }

    [Fact]
    public void Increment_SingleCall_ReadReturnsOne()
    {
        var sut = new SearchZeroResultsCounter();

        sut.Increment();

        Assert.Equal(1L, sut.Read());
    }

    [Fact]
    public void Increment_TenThousandParallelCalls_ReadReturnsExactlyTenThousand()
    {
        var sut = new SearchZeroResultsCounter();

        Parallel.For(0, 10_000, _ => sut.Increment());

        Assert.Equal(10_000L, sut.Read());
    }
}
