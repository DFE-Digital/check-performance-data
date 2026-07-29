using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using DfE.CheckPerformanceData.Web.Controllers;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    // --- Explicit-bucket-size overload: caller-picked granularity ---

    [Fact]
    public async Task GetVolumeOverTime_ExplicitFifteenMinutes_ReturnsQuarterHourBuckets()
    {
        await ResetSearchEventsAsync();

        // Align to a 15-minute boundary so the assertion counts full buckets on both ends.
        var now = QuarterHourAligned(DateTime.UtcNow);
        var from = now.AddHours(-1);

        // One event per 15-min slot → 4 buckets, each with SearchCount = 1.
        await SeedAsync(
            NewEvent(from.AddMinutes(1),  "s-1", "q", results: 1, latency: 10),
            NewEvent(from.AddMinutes(16), "s-2", "q", results: 1, latency: 10),
            NewEvent(from.AddMinutes(31), "s-3", "q", results: 1, latency: 10),
            NewEvent(from.AddMinutes(46), "s-4", "q", results: 1, latency: 10));

        var buckets = await CreateService().GetVolumeOverTimeAsync(
            from, now, VolumeBucketSize.FifteenMinutes, CancellationToken.None);

        Assert.Equal(4, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(1, b.SearchCount));
        Assert.All(buckets, b => Assert.Equal(1, b.UniqueSessionCount));
    }

    [Fact]
    public async Task GetVolumeOverTime_ExplicitHour_ReturnsHourBuckets()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-6);

        for (var i = 0; i < 6; i++)
        {
            await SeedAsync(NewEvent(from.AddHours(i).AddMinutes(30), $"s-h-{i}", "q", results: 1, latency: 10));
        }

        var buckets = await CreateService().GetVolumeOverTimeAsync(
            from, now, VolumeBucketSize.Hour, CancellationToken.None);

        Assert.Equal(6, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(1, b.SearchCount));
    }

    [Fact]
    public async Task GetVolumeOverTime_ExplicitDay_ReturnsDayBuckets()
    {
        await ResetSearchEventsAsync();

        var now = DayAligned(DateTime.UtcNow);
        var from = now.AddDays(-4);

        for (var i = 0; i < 4; i++)
        {
            await SeedAsync(NewEvent(from.AddDays(i).AddHours(6), $"s-d-{i}", "q", results: 1, latency: 10));
        }

        var buckets = await CreateService().GetVolumeOverTimeAsync(
            from, now, VolumeBucketSize.Day, CancellationToken.None);

        Assert.Equal(4, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(1, b.SearchCount));
    }

    [Fact]
    public async Task GetVolumeOverTime_ExplicitWeek_ReturnsWeekBuckets()
    {
        await ResetSearchEventsAsync();

        // Anchor to Monday 00:00 UTC — Postgres date_trunc('week', ts) uses ISO Monday.
        var now = MondayAligned(DateTime.UtcNow);
        var from = now.AddDays(-14);

        // Two events, one in each of the two full weeks.
        await SeedAsync(
            NewEvent(from.AddDays(2),  "s-w-1", "q", results: 1, latency: 10),
            NewEvent(from.AddDays(9),  "s-w-2", "q", results: 1, latency: 10));

        var buckets = await CreateService().GetVolumeOverTimeAsync(
            from, now, VolumeBucketSize.Week, CancellationToken.None);

        Assert.Equal(2, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(1, b.SearchCount));
    }

    [Fact]
    public async Task GetVolumeOverTime_ExplicitMonth_ReturnsMonthBuckets()
    {
        await ResetSearchEventsAsync();

        // Anchor to the first of the current month — Postgres date_trunc('month', ts).
        var now = MonthAligned(DateTime.UtcNow);
        var from = MonthAligned(now.AddMonths(-2));

        await SeedAsync(
            NewEvent(from.AddDays(3),               "s-m-1", "q", results: 1, latency: 10),
            NewEvent(from.AddMonths(1).AddDays(3),  "s-m-2", "q", results: 1, latency: 10));

        var buckets = await CreateService().GetVolumeOverTimeAsync(
            from, now, VolumeBucketSize.Month, CancellationToken.None);

        Assert.Equal(2, buckets.Count);
        Assert.All(buckets, b => Assert.Equal(1, b.SearchCount));
    }

    // --- GetUniqueSessionsOverTimeAsync — distinct session_id count per bucket ---

    [Fact]
    public async Task GetUniqueSessionsOverTime_CountsDistinctSessionsPerBucket()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-3);

        // Bucket 0: 3 events across 2 distinct sessions.
        // Bucket 1: 5 events all in the same session.
        // Bucket 2: empty (must gap-fill to 0).
        await SeedAsync(
            NewEvent(from.AddMinutes(10), "s-a", "q", results: 1, latency: 10),
            NewEvent(from.AddMinutes(20), "s-a", "q", results: 1, latency: 10),
            NewEvent(from.AddMinutes(30), "s-b", "q", results: 1, latency: 10),
            NewEvent(from.AddHours(1).AddMinutes(5),  "s-c", "q", results: 1, latency: 10),
            NewEvent(from.AddHours(1).AddMinutes(10), "s-c", "q", results: 1, latency: 10),
            NewEvent(from.AddHours(1).AddMinutes(15), "s-c", "q", results: 1, latency: 10),
            NewEvent(from.AddHours(1).AddMinutes(20), "s-c", "q", results: 1, latency: 10),
            NewEvent(from.AddHours(1).AddMinutes(25), "s-c", "q", results: 1, latency: 10));

        var buckets = await CreateService().GetUniqueSessionsOverTimeAsync(
            from, now, VolumeBucketSize.Hour, CancellationToken.None);

        Assert.Equal(3, buckets.Count);
        Assert.Equal(2, buckets[0].SearchCount);
        Assert.Equal(1, buckets[1].SearchCount);
        Assert.Equal(0, buckets[2].SearchCount);
    }

    [Fact]
    public async Task GetUniqueSessionsOverTime_GapFillsEmptyBucketsToZero()
    {
        await ResetSearchEventsAsync();

        var now = QuarterHourAligned(DateTime.UtcNow);
        var from = now.AddHours(-1);

        await SeedAsync(NewEvent(from.AddMinutes(2), "s-only", "q", results: 1, latency: 10));

        var buckets = await CreateService().GetUniqueSessionsOverTimeAsync(
            from, now, VolumeBucketSize.FifteenMinutes, CancellationToken.None);

        Assert.Equal(4, buckets.Count);
        Assert.Equal(1, buckets[0].SearchCount);
        Assert.Equal(0, buckets[1].SearchCount);
        Assert.Equal(0, buckets[2].SearchCount);
        Assert.Equal(0, buckets[3].SearchCount);
    }

    // --- GetZeroResultCountOverTimeAsync — zero-result event count per bucket ---

    [Fact]
    public async Task GetZeroResultCountOverTime_CountsOnlyZeroResultEventsPerBucket()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-3);

        // Bucket 0: 2 zero-result, 3 with results → count = 2.
        // Bucket 1: 0 zero-result, 4 with results → count = 0.
        // Bucket 2: 5 zero-result → count = 5.
        await SeedAsync(
            NewEvent(from.AddMinutes(5),  "s-1", "q", results: 0, latency: 10),
            NewEvent(from.AddMinutes(10), "s-2", "q", results: 0, latency: 10),
            NewEvent(from.AddMinutes(15), "s-3", "q", results: 3, latency: 10),
            NewEvent(from.AddMinutes(20), "s-4", "q", results: 3, latency: 10),
            NewEvent(from.AddMinutes(25), "s-5", "q", results: 3, latency: 10),
            NewEvent(from.AddHours(1).AddMinutes(5),  "s-6", "q", results: 4, latency: 10),
            NewEvent(from.AddHours(1).AddMinutes(15), "s-7", "q", results: 4, latency: 10),
            NewEvent(from.AddHours(1).AddMinutes(25), "s-8", "q", results: 4, latency: 10),
            NewEvent(from.AddHours(1).AddMinutes(35), "s-9", "q", results: 4, latency: 10),
            NewEvent(from.AddHours(2).AddMinutes(5),  "s-a", "q", results: 0, latency: 10),
            NewEvent(from.AddHours(2).AddMinutes(10), "s-b", "q", results: 0, latency: 10),
            NewEvent(from.AddHours(2).AddMinutes(15), "s-c", "q", results: 0, latency: 10),
            NewEvent(from.AddHours(2).AddMinutes(20), "s-d", "q", results: 0, latency: 10),
            NewEvent(from.AddHours(2).AddMinutes(25), "s-e", "q", results: 0, latency: 10));

        var buckets = await CreateService().GetZeroResultCountOverTimeAsync(
            from, now, VolumeBucketSize.Hour, CancellationToken.None);

        Assert.Equal(3, buckets.Count);
        Assert.Equal(2, buckets[0].SearchCount);
        Assert.Equal(0, buckets[1].SearchCount);
        Assert.Equal(5, buckets[2].SearchCount);
    }

    [Fact]
    public async Task GetZeroResultCountOverTime_GapFillsEmptyBucketsToZero()
    {
        await ResetSearchEventsAsync();

        var now = QuarterHourAligned(DateTime.UtcNow);
        var from = now.AddHours(-1);

        // Zero-result event in bucket 0 only. Other buckets have no rows.
        await SeedAsync(NewEvent(from.AddMinutes(2), "s-1", "q", results: 0, latency: 10));

        var buckets = await CreateService().GetZeroResultCountOverTimeAsync(
            from, now, VolumeBucketSize.FifteenMinutes, CancellationToken.None);

        Assert.Equal(4, buckets.Count);
        Assert.Equal(1, buckets[0].SearchCount);
        Assert.Equal(0, buckets[1].SearchCount);
        Assert.Equal(0, buckets[2].SearchCount);
        Assert.Equal(0, buckets[3].SearchCount);
    }

    // --- GetLatencyPercentilesOverTimeAsync — p5 / p50 / p95 per bucket ---

    [Fact]
    public async Task GetLatencyPercentilesOverTime_ReturnsPercentilesPerBucket()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-2);

        // Bucket 0: latencies {10, 20, 30, 40, 50, 60, 70, 80, 90, 100} → p5 ≈ 14.5, p50 = 55, p95 ≈ 95.5.
        // Bucket 1: latencies {200, 200, 200, 200, 200} → p5 = p50 = p95 = 200.
        for (var i = 1; i <= 10; i++)
        {
            await SeedAsync(NewEvent(from.AddMinutes(5 * i), $"s-{i}", "q", results: 1, latency: i * 10));
        }
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(NewEvent(from.AddHours(1).AddMinutes(5 + i * 5), $"s-b-{i}", "q", results: 1, latency: 200));
        }

        var buckets = await CreateService().GetLatencyPercentilesOverTimeAsync(
            from, now, VolumeBucketSize.Hour, CancellationToken.None);

        Assert.Equal(2, buckets.Count);
        Assert.InRange(buckets[0].P5Ms, 10, 20);
        Assert.InRange(buckets[0].P50Ms, 50, 60);
        Assert.InRange(buckets[0].P95Ms, 90, 100);
        Assert.Equal(200, buckets[1].P5Ms);
        Assert.Equal(200, buckets[1].P50Ms);
        Assert.Equal(200, buckets[1].P95Ms);
    }

    [Fact]
    public async Task GetLatencyPercentilesOverTime_GapFillsEmptyBucketsWithZeroPercentiles()
    {
        await ResetSearchEventsAsync();

        var now = QuarterHourAligned(DateTime.UtcNow);
        var from = now.AddHours(-1);

        // One event in bucket 0. Other three buckets are empty and must gap-fill to 0/0/0 —
        // NOT null / NOT NaN / NOT omitted — the client renders continuous polylines that
        // require a point for every bucket on the X-axis spine.
        await SeedAsync(NewEvent(from.AddMinutes(2), "s-1", "q", results: 1, latency: 42));

        var buckets = await CreateService().GetLatencyPercentilesOverTimeAsync(
            from, now, VolumeBucketSize.FifteenMinutes, CancellationToken.None);

        Assert.Equal(4, buckets.Count);
        Assert.Equal(42, buckets[0].P50Ms);
        Assert.Equal(0, buckets[1].P5Ms);
        Assert.Equal(0, buckets[1].P50Ms);
        Assert.Equal(0, buckets[1].P95Ms);
        Assert.Equal(0, buckets[2].P95Ms);
        Assert.Equal(0, buckets[3].P95Ms);
    }

    [Fact]
    public async Task GetLatencyPercentilesOverTime_HonoursWindow_ExcludesRowsOutside()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-1);

        // Row inside the window (latency 25) and one OUTSIDE the window (latency 999) that
        // must NOT contribute to any percentile.
        await SeedAsync(
            NewEvent(from.AddMinutes(10), "s-in", "q", results: 1, latency: 25),
            NewEvent(from.AddHours(-2), "s-out", "q", results: 1, latency: 999));

        var buckets = await CreateService().GetLatencyPercentilesOverTimeAsync(
            from, now, VolumeBucketSize.Hour, CancellationToken.None);

        // 1 bucket, all percentiles = 25 (single value → percentile_cont returns the value itself).
        Assert.Single(buckets);
        Assert.Equal(25, buckets[0].P50Ms);
        Assert.Equal(25, buckets[0].P5Ms);
        Assert.Equal(25, buckets[0].P95Ms);
    }

    [Fact]
    public async Task GetVolumeOverTime_ExplicitBucket_GapFillsEmptyBucketsToZero()
    {
        await ResetSearchEventsAsync();

        var now = QuarterHourAligned(DateTime.UtcNow);
        var from = now.AddHours(-1);

        // Event only in the first quarter. The other three MUST render as zero-filled buckets.
        await SeedAsync(NewEvent(from.AddMinutes(2), "s-1", "q", results: 1, latency: 10));

        var buckets = await CreateService().GetVolumeOverTimeAsync(
            from, now, VolumeBucketSize.FifteenMinutes, CancellationToken.None);

        Assert.Equal(4, buckets.Count);
        Assert.Equal(1, buckets[0].SearchCount);
        Assert.Equal(0, buckets[1].SearchCount);
        Assert.Equal(0, buckets[2].SearchCount);
        Assert.Equal(0, buckets[3].SearchCount);
    }

    // --- Paged time-series readers: same zero-filled spine, sliced by page + pageSize ---

    [Fact]
    public async Task GetPagedVolumeOverTime_SecondPage_ReturnsSliceAndTotalCount()
    {
        await ResetSearchEventsAsync();

        // 100 hour-buckets so page 2 of pageSize 20 lands squarely in the middle.
        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-100);

        var events = new List<SearchEvent>();
        for (var h = 0; h < 100; h++)
        {
            events.Add(NewEvent(from.AddHours(h).AddMinutes(15), $"s-{h}", "q", results: 1, latency: 10));
        }
        await SeedAsync(events.ToArray());

        var (rows, total) = await CreateService().GetPagedVolumeOverTimeAsync(
            from, now, VolumeBucketSize.Hour, page: 2, pageSize: 20, CancellationToken.None);

        Assert.Equal(100, total);
        Assert.Equal(20, rows.Count);
        // Every bucket has one event → SearchCount == 1 across every slice.
        Assert.All(rows, r => Assert.Equal(1, r.SearchCount));
        // Page 2 starts at bucket index 20 (i.e. hour offset 20 from the window start).
        Assert.Equal(from.AddHours(20), rows[0].BucketStart);
        Assert.Equal(from.AddHours(39), rows[^1].BucketStart);
    }

    [Fact]
    public async Task GetPagedVolumeOverTime_ZeroFillsEmptyBucketsAcrossPages()
    {
        await ResetSearchEventsAsync();

        // 30 hour-buckets, one event only in bucket 15 → pages either side should be all zero rows.
        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-30);

        await SeedAsync(NewEvent(from.AddHours(15).AddMinutes(15), "s-only", "q", results: 1, latency: 10));

        var svc = CreateService();
        var (pg1, total) = await svc.GetPagedVolumeOverTimeAsync(
            from, now, VolumeBucketSize.Hour, page: 1, pageSize: 10, CancellationToken.None);
        var (pg2, _) = await svc.GetPagedVolumeOverTimeAsync(
            from, now, VolumeBucketSize.Hour, page: 2, pageSize: 10, CancellationToken.None);
        var (pg3, _) = await svc.GetPagedVolumeOverTimeAsync(
            from, now, VolumeBucketSize.Hour, page: 3, pageSize: 10, CancellationToken.None);

        Assert.Equal(30, total);
        Assert.All(pg1, r => Assert.Equal(0, r.SearchCount));
        // Bucket 15 lives on page 2 (rows 11..20). Zero-indexed within the page it is
        // (bucket-index 15) - (page-2 offset 10) = 5.
        Assert.Equal(1, pg2[5].SearchCount);
        Assert.All(pg3, r => Assert.Equal(0, r.SearchCount));
    }

    [Fact]
    public async Task GetPagedUniqueSessionsOverTime_ReturnsDistinctSessionSlice()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-50);

        // Every bucket carries 2 distinct sessions.
        var events = new List<SearchEvent>();
        for (var h = 0; h < 50; h++)
        {
            events.Add(NewEvent(from.AddHours(h).AddMinutes(10), $"s-a-{h}", "q", results: 1, latency: 10));
            events.Add(NewEvent(from.AddHours(h).AddMinutes(20), $"s-b-{h}", "q", results: 1, latency: 10));
        }
        await SeedAsync(events.ToArray());

        var (rows, total) = await CreateService().GetPagedUniqueSessionsOverTimeAsync(
            from, now, VolumeBucketSize.Hour, page: 2, pageSize: 20, CancellationToken.None);

        Assert.Equal(50, total);
        Assert.Equal(20, rows.Count);
        Assert.All(rows, r => Assert.Equal(2, r.SearchCount));
    }

    [Fact]
    public async Task GetPagedZeroResultCountOverTime_ReturnsZeroResultSlice()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-40);

        // Every bucket has 3 zero-result events + 1 non-zero.
        var events = new List<SearchEvent>();
        for (var h = 0; h < 40; h++)
        {
            events.Add(NewEvent(from.AddHours(h).AddMinutes(5),  $"s-{h}-1", "q", results: 0, latency: 10));
            events.Add(NewEvent(from.AddHours(h).AddMinutes(10), $"s-{h}-2", "q", results: 0, latency: 10));
            events.Add(NewEvent(from.AddHours(h).AddMinutes(15), $"s-{h}-3", "q", results: 0, latency: 10));
            events.Add(NewEvent(from.AddHours(h).AddMinutes(20), $"s-{h}-4", "q", results: 5, latency: 10));
        }
        await SeedAsync(events.ToArray());

        var (rows, total) = await CreateService().GetPagedZeroResultCountOverTimeAsync(
            from, now, VolumeBucketSize.Hour, page: 1, pageSize: 20, CancellationToken.None);

        Assert.Equal(40, total);
        Assert.Equal(20, rows.Count);
        Assert.All(rows, r => Assert.Equal(3, r.SearchCount));
    }

    [Fact]
    public async Task GetPagedLatencyPercentilesOverTime_ReturnsPercentileSlice()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-30);

        // Every bucket has 5 events of latency 200ms → p5=p50=p95=200 across every slice.
        var events = new List<SearchEvent>();
        for (var h = 0; h < 30; h++)
        {
            for (var i = 0; i < 5; i++)
            {
                events.Add(NewEvent(from.AddHours(h).AddMinutes(5 + i * 3), $"s-{h}-{i}", "q", results: 1, latency: 200));
            }
        }
        await SeedAsync(events.ToArray());

        var (rows, total) = await CreateService().GetPagedLatencyPercentilesOverTimeAsync(
            from, now, VolumeBucketSize.Hour, page: 2, pageSize: 10, CancellationToken.None);

        Assert.Equal(30, total);
        Assert.Equal(10, rows.Count);
        Assert.All(rows, r => Assert.Equal(200, r.P5Ms));
        Assert.All(rows, r => Assert.Equal(200, r.P50Ms));
        Assert.All(rows, r => Assert.Equal(200, r.P95Ms));
    }

    [Fact]
    public async Task GetPagedVolumeOverTime_LastPagePartial_ReturnsRemainingBuckets()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddHours(-25);

        var svc = CreateService();
        var (rows, total) = await svc.GetPagedVolumeOverTimeAsync(
            from, now, VolumeBucketSize.Hour, page: 3, pageSize: 10, CancellationToken.None);

        Assert.Equal(25, total);
        // Last page holds the remaining 5 buckets (25 - 20 = 5).
        Assert.Equal(5, rows.Count);
    }

    // --- Chart partial renders SVG + a WCAG 1.1.1 <details>/<table> fallback ---

    [Fact]
    public async Task VolumeChartPartial_WithBuckets_RendersSvgAndDetailsTableFallback()
    {
        var buckets = new List<VolumeBucket>();
        var now = HourAligned(DateTime.UtcNow);
        for (var i = 0; i < 6; i++)
            buckets.Add(new VolumeBucket(now.AddHours(-6 + i), SearchCount: 10 + i, UniqueSessionCount: 5 + i));

        var html = await RenderVolumeChartPartialAsync(buckets);

        Assert.Contains("<svg", html);
        Assert.Contains("sa-chart", html);
        // WCAG 1.1.1: a data-table fallback lives beneath every chart.
        Assert.Contains("<details class=\"govuk-details\"", html);
        // Every bucket appears in the fallback table (6 rows).
        var trCount = System.Text.RegularExpressions.Regex.Matches(html, "<tr").Count;
        Assert.True(trCount >= 6, $"Expected at least 6 <tr> rows in the fallback table, got {trCount}.");
        // Series are distinguished by SHAPE + colour, not colour alone: blue rect on Y1, green
        // circle on Y2. Assert both marker shapes appear.
        Assert.Contains("<rect", html);
        Assert.Contains("<circle", html);
        // GDS palette only: blue and green tokens.
        Assert.Contains("#1d70b8", html);
        Assert.Contains("#00703c", html);
    }

    [Fact]
    public async Task VolumeChartPartial_WithNoBuckets_RendersEmptyStateNotSvg()
    {
        var html = await RenderVolumeChartPartialAsync(Array.Empty<VolumeBucket>());

        // An empty series must not silently render an SVG with zero points — the empty state
        // gives the admin a readable "no data" message instead.
        Assert.DoesNotContain("<polyline", html);
        Assert.Contains("No data", html, StringComparison.OrdinalIgnoreCase);
    }

    // Standalone render of the _VolumeChart.cshtml partial via the composite view engine.
    // Same shape as SearchAnalyticsIndexViewRenderTests — no _AdminLayout dependency graph
    // wiring needed because the partial is self-contained (no layout, no view components).
    private static async Task<string> RenderVolumeChartPartialAsync(IReadOnlyList<VolumeBucket> model)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(SearchAnalyticsController).Assembly);
                    services.AddGovUkFrontend();
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var routeData = new RouteData();
        routeData.Values["controller"] = "SearchAnalytics";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.GetView(executingFilePath: null,
            viewPath: "/Views/Admin/Search/_VolumeChart.cshtml", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate _VolumeChart partial. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<IReadOnlyList<VolumeBucket>>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model,
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }

    // --- Helpers ---

    private static DateTime HourAligned(DateTime value) =>
        DateTime.SpecifyKind(new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0), DateTimeKind.Utc);

    private static DateTime DayAligned(DateTime value) =>
        DateTime.SpecifyKind(new DateTime(value.Year, value.Month, value.Day, 0, 0, 0), DateTimeKind.Utc);

    private static DateTime QuarterHourAligned(DateTime value)
    {
        var floorMinutes = (value.Minute / 15) * 15;
        return DateTime.SpecifyKind(
            new DateTime(value.Year, value.Month, value.Day, value.Hour, floorMinutes, 0),
            DateTimeKind.Utc);
    }

    private static DateTime MondayAligned(DateTime value)
    {
        var day = DateTime.SpecifyKind(new DateTime(value.Year, value.Month, value.Day, 0, 0, 0), DateTimeKind.Utc);
        var offset = ((int)day.DayOfWeek + 6) % 7; // Monday=0..Sunday=6
        return day.AddDays(-offset);
    }

    private static DateTime MonthAligned(DateTime value) =>
        DateTime.SpecifyKind(new DateTime(value.Year, value.Month, 1, 0, 0, 0), DateTimeKind.Utc);

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

    // --- Aggregate-to-typical-week readers -------------------------------------

    // 30 days of events, one event per hour on every Monday at 09:00 UTC in the window.
    // The aggregate reader collapses every Monday-09 event into the (Mon, 09:00) cell of
    // the cyclic 7-day timeline; the assertion pins the summed count in that cell equals
    // the number of Mondays in the window.
    [Fact]
    public async Task GetVolumeAggregatedByWeekdayHour_SumsAllEventsAtThatWeekdayHour()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddDays(-30);

        // Seed one event on every Monday between from and now at 09:00 UTC.
        var events = new List<SearchEvent>();
        var mondays = 0;
        for (var d = 0; d < 30; d++)
        {
            var slot = from.AddDays(d).AddHours(9);
            if (slot.DayOfWeek == DayOfWeek.Monday)
            {
                events.Add(NewEvent(slot, $"m-{d}", "q", results: 1, latency: 10));
                mondays++;
            }
        }
        // Sanity — 30-day span always includes at least 3-5 Mondays.
        Assert.True(mondays >= 3);
        await SeedAsync(events.ToArray());

        var buckets = await CreateService()
            .GetVolumeAggregatedByWeekdayHourAsync(from, now, CancellationToken.None);

        // Full 168-cell grid, zero-filled.
        Assert.Equal(168, buckets.Count);
        // Total across every cell equals the total number of events seeded.
        Assert.Equal(mondays, buckets.Sum(b => b.SearchCount));
        // Every Monday 09:00 event landed in exactly one cell — find the cell whose
        // BucketStart's weekday is Monday and hour is 9.
        var mondayNine = buckets.Where(b => b.BucketStart.DayOfWeek == DayOfWeek.Monday
                                          && b.BucketStart.Hour == 9).ToList();
        Assert.Single(mondayNine);
        Assert.Equal(mondays, mondayNine[0].SearchCount);
    }

    [Fact]
    public async Task GetLatencyPercentilesAggregatedByWeekdayHour_ComputesPerCyclicCell()
    {
        await ResetSearchEventsAsync();

        var now = HourAligned(DateTime.UtcNow);
        var from = now.AddDays(-14);

        // Seed 5 events at each Monday-09 slot with fixed latency = 100 so the
        // percentile trio collapses to p5 = p50 = p95 = 100 for that cell.
        var events = new List<SearchEvent>();
        for (var d = 0; d < 14; d++)
        {
            var slot = from.AddDays(d).AddHours(9);
            if (slot.DayOfWeek != DayOfWeek.Monday) continue;
            for (var i = 0; i < 5; i++)
            {
                events.Add(NewEvent(slot.AddMinutes(i), $"l-{d}-{i}", "q", results: 1, latency: 100));
            }
        }
        await SeedAsync(events.ToArray());

        var buckets = await CreateService()
            .GetLatencyPercentilesAggregatedByWeekdayHourAsync(from, now, CancellationToken.None);

        Assert.Equal(168, buckets.Count);
        var mondayNine = buckets.Single(b => b.BucketStart.DayOfWeek == DayOfWeek.Monday
                                          && b.BucketStart.Hour == 9);
        Assert.Equal(100, mondayNine.P5Ms);
        Assert.Equal(100, mondayNine.P50Ms);
        Assert.Equal(100, mondayNine.P95Ms);
    }
}
