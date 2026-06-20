using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Application.UnitTests.Observability;

// The transactions sort key resolves against a fixed allow-list before reaching the ORDER BY, so
// a hand-edited ?sort= value can never become raw SQL: an unknown key snaps to the default.
public sealed class TransactionSortTests
{
    [Theory]
    [InlineData("time", "recorded_at_utc")]
    [InlineData("reference", "reference_number")]
    [InlineData("stage", "stage")]
    [InlineData("queue", "queue_name")]
    [InlineData("decision", "decision_status")]
    [InlineData("latency", "latency_ms")]
    public void Column_MapsEachKnownKeyToItsColumn(string key, string expectedColumn)
    {
        Assert.Equal(expectedColumn, TransactionSort.Column(TransactionSort.Resolve(key)));
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.Equal("reference", TransactionSort.Resolve("Reference"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("recorded_at_utc")] // a column name is not a key
    [InlineData("1; DROP TABLE queue_metrics_events")]
    public void Resolve_UnknownKey_FallsBackToTheDefault(string? key)
    {
        Assert.Equal(TransactionSort.DefaultKey, TransactionSort.Resolve(key));
    }

    [Fact]
    public void Column_UnknownKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => TransactionSort.Column("bogus"));
    }
}
