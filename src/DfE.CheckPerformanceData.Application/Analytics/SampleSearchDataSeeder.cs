namespace DfE.CheckPerformanceData.Application.Analytics;

// Development-only seeder that fabricates a plausible mix of search-event + feedback-message
// rows across a chosen time span so the search-analytics dashboard has something meaningful
// to demo. Stubbed here; the failing integration test locks the contract before the real
// distribution logic lands in the GREEN commit.
public sealed class SampleSearchDataSeeder(
    ISearchAnalyticsSink sink,
    ISampleSearchDataGateway messagesGateway)
{
    public Task<SampleSearchDataSeedResult> SeedAsync(
        TimeSpan span,
        int eventCount,
        int messageCount,
        DateTime nowUtc,
        int seed,
        CancellationToken cancellationToken)
    {
        _ = sink;
        _ = messagesGateway;
        _ = span;
        _ = eventCount;
        _ = messageCount;
        _ = nowUtc;
        _ = seed;
        _ = cancellationToken;
        throw new NotImplementedException("SampleSearchDataSeeder.SeedAsync — impl lands in GREEN.");
    }
}

public sealed record SampleSearchDataSeedResult(int EventsCreated, int ResultsCreated, int MessagesCreated);
