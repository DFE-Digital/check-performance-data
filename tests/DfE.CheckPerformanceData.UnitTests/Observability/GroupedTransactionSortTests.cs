using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Application.UnitTests.Observability;

// The grouped-transactions sort key resolves against its own fixed allow-list before reaching the
// ORDER BY, exactly like the flat-list TransactionSort: a hand-edited ?sort= value can never become
// raw SQL — an unknown key snaps to the default (most recently active first). The keys are the
// stable identifiers the grouped column headers submit; the values are server-side ORDER BY
// expressions (some are derived waits, never caller text).
public sealed class GroupedTransactionSortTests
{
    [Theory]
    [InlineData("reference", "reference_number")]
    [InlineData("submit", "submitted_at")]
    [InlineData("rulesqueue", "(rules_started - submitted_at)")]
    [InlineData("rulesengine", "(rules_at - rules_started)")]
    [InlineData("status", "decision")]
    [InlineData("zendeskqueue", "(ticket_started - rules_at)")]
    [InlineData("zendeskticket", "ticket_at")]
    [InlineData("last", "last_at")]
    public void OrderExpression_MapsEachKnownKeyToItsExpression(string key, string expected)
    {
        Assert.Equal(expected, GroupedTransactionSort.OrderExpression(GroupedTransactionSort.Resolve(key)));
    }

    [Fact]
    public void DefaultKey_IsLastActivity()
    {
        Assert.Equal("last", GroupedTransactionSort.DefaultKey);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.Equal("submit", GroupedTransactionSort.Resolve("Submit"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("submitted_at")] // a column name is not a key
    [InlineData("time")]          // a flat-list key is not a grouped key
    [InlineData("1; DROP TABLE queue_metrics_events")]
    public void Resolve_UnknownKey_FallsBackToTheDefault(string? key)
    {
        Assert.Equal(GroupedTransactionSort.DefaultKey, GroupedTransactionSort.Resolve(key));
    }

    [Fact]
    public void OrderExpression_UnknownKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => GroupedTransactionSort.OrderExpression("bogus"));
    }
}
