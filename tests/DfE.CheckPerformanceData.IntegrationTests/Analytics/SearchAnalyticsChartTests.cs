using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Read-side tests for the volume-over-time chart feed. The chart reads a bucketed count of
// search events + unique sessions per bucket, gap-filled to zero via a generate_series spine
// so a window with no rows in a bucket still emits that bucket at 0. Windows of 48 hours or
// less bucket by hour; anything wider buckets by day. Every bucket bound + generate_series
// step is server-computed and parameterised — the read path has no injection surface.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class SearchAnalyticsChartTests
{
    private readonly PostgresFixture _fixture;

    public SearchAnalyticsChartTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- 24-hour window buckets by hour ---

    [Fact]
    public async Task GetVolumeOverTime_TwentyFourHourWindow_ReturnsTwentyFourHourBuckets()
    {
        await ResetSearchEventsAsync();

        // Anchor the window on hour-aligned boundaries so the assertion counts hour-buckets, not
        // partial hours at either end (a request straddling a bucket edge would emit 25 buckets).
        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-24);

        // 10 sessions posting one event per hour inside the window = 240 rows / 10 per bucket.
        // Plus 10 sessions posting one event per hour in the 24h BEFORE the window = 240 rows
        // strictly outside; the bucketed count must ignore them.
        var events = new List<SearchEvent>();
        for (var h = 0; h < 24; h++)
        {
            for (var s = 0; s < 10; s++)
            {
                // +30 min offsets the event to the middle of the bucket so it lands unambiguously.
                events.Add(NewEvent(from.AddHours(h).AddMinutes(30), $"s-{s}", "q", results: 1, latency: 10));
            }
        }
        // Rows just OUTSIDE the window that must NOT be counted.
        for (var h = 0; h < 24; h++)
            events.Add(NewEvent(from.AddHours(-h - 1), $"s-out-{h}", "old", results: 0, latency: 5));

        await SeedAsync(events.ToArray());

        var buckets = await CreateService().GetVolumeOverTimeAsync(from, now, CancellationToken.None);

        Assert.Equal(24, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(10, b.SearchCount));
        Assert.All(buckets, b => Assert.Equal(10, b.UniqueSessionCount));
    }

    // --- Wider window buckets by day ---

    [Fact]
    public async Task GetVolumeOverTime_ThirtyDayWindow_ReturnsThirtyDayBuckets()
    {
        await ResetSearchEventsAsync();

        var now = DayAligned(DateTime.UtcNow);
        var from = now.AddDays(-30);

        // One event per day for 30 days from 3 distinct sessions.
        var events = new List<SearchEvent>();
        for (var d = 0; d < 30; d++)
        {
            for (var s = 0; s < 3; s++)
            {
                events.Add(NewEvent(from.AddDays(d).AddHours(6), $"day-s-{s}", "q", results: 1, latency: 10));
            }
        }

        await SeedAsync(events.ToArray());

        var buckets = await CreateService().GetVolumeOverTimeAsync(from, now, CancellationToken.None);

        Assert.Equal(30, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(3, b.SearchCount));
        Assert.All(buckets, b => Assert.Equal(3, b.UniqueSessionCount));
    }

    // --- Empty buckets in the middle of the range must be zero-filled ---

    [Fact]
    public async Task GetVolumeOverTime_MiddleBucketWithNoRows_IsZeroFilledViaGenerateSeries()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-6);

        // Rows only in the first and last hour buckets — the four buckets between MUST render as 0.
        await SeedAsync(
            NewEvent(from.AddMinutes(15), "s-early", "q", results: 1, latency: 10),
            NewEvent(from.AddMinutes(45), "s-early-2", "q", results: 1, latency: 10),
            NewEvent(from.AddHours(5).AddMinutes(15), "s-late", "q", results: 1, latency: 10));

        var buckets = await CreateService().GetVolumeOverTimeAsync(from, now, CancellationToken.None);

        Assert.Equal(6, buckets.Count);
        Assert.Equal(2, buckets[0].SearchCount);
        Assert.Equal(2, buckets[0].UniqueSessionCount);
        Assert.Equal(0, buckets[1].SearchCount);
        Assert.Equal(0, buckets[2].SearchCount);
        Assert.Equal(0, buckets[3].SearchCount);
        Assert.Equal(0, buckets[4].SearchCount);
        Assert.Equal(1, buckets[5].SearchCount);
        Assert.Equal(1, buckets[5].UniqueSessionCount);
    }

    // --- 48-hour boundary picks hour-granularity; 49 hours picks day-granularity ---

    [Fact]
    public async Task GetVolumeOverTime_FortyEightHourWindow_UsesHourBuckets()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-48);

        await SeedAsync(NewEvent(from.AddMinutes(15), "s-a", "q", results: 1, latency: 10));

        var buckets = await CreateService().GetVolumeOverTimeAsync(from, now, CancellationToken.None);

        Assert.Equal(48, buckets.Count);
    }

    [Fact]
    public async Task GetVolumeOverTime_FortyNineHourWindow_UsesDayBuckets()
    {
        await ResetSearchEventsAsync();

        var now = DayAligned(DateTime.UtcNow);
        var from = now.AddHours(-49);

        await SeedAsync(NewEvent(from.AddMinutes(15), "s-a", "q", results: 1, latency: 10));

        var buckets = await CreateService().GetVolumeOverTimeAsync(from, now, CancellationToken.None);

        // 49h spans 3 calendar days (day 0 partial, day 1 full, day 2 up to now).
        Assert.True(buckets.Count is >= 2 and <= 3, $"Expected 2..3 day-buckets across a 49h window, got {buckets.Count}");
    }

    // --- Helpers ---

    private static DateTime HourAligned(DateTime value) =>
        DateTime.SpecifyKind(new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0), DateTimeKind.Utc);

    private static DateTime DayAligned(DateTime value) =>
        DateTime.SpecifyKind(new DateTime(value.Year, value.Month, value.Day, 0, 0, 0), DateTimeKind.Utc);

    private ISearchAnalyticsQueryService CreateService() =>
        new SearchAnalyticsQueryService(_fixture.CreateContext());

    private static SearchEvent NewEvent(
        DateTime occurredAtUtc,
        string sessionId,
        string queryNormalised,
        int results,
        int latency) => new()
        {
            OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc),
            SessionId = sessionId,
            QueryRaw = queryNormalised,
            QueryNormalised = queryNormalised,
            Scope = null,
            ResultsPages = results,
            ResultsBlocks = 0,
            LatencyMs = latency,
        };

    private async Task SeedAsync(params SearchEvent[] events)
    {
        await using var context = _fixture.CreateContext();
        context.SearchEvents.AddRange(events);
        await context.SaveChangesAsync();
    }

    private async Task ResetSearchEventsAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE search_events RESTART IDENTITY CASCADE;");
    }
}
