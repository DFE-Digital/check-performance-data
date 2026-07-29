using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Contract tests for the multi-scale variance the seeder layers on top of its static
// weekday × hour weights so a 90-day quarter (or 365-day year) doesn't come out as
// twelve near-identical weeks. Lance's report: "every week in the resulting dashboard
// looks pretty much the same — no big spikes, no dips, no visible variance week-to-
// week." Variance comes from three independent stacked sources:
//   1. Weekly multiplier — one draw per ISO week, [0.5, 1.8] with light autocorrelation.
//   2. Daily anomalies — ~5% "spike days" (2×-3×) and ~10% "quiet days" (0.2×-0.4×).
//   3. Outage bursts — 1-2 per quarter, 3-6 h contiguous windows with elevated latency
//      and depressed volume.
// All driven by the seeder's deterministic RNG so the same session id + preset still
// reproduces the same output.
[Collection(nameof(PostgresCollection))]
public sealed class SampleSearchDataVarianceTests
{
    private readonly PostgresFixture _fixture;

    public SampleSearchDataVarianceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private SampleSearchDataSeeder CreateSut() =>
        new(
            new DbSearchAnalyticsSink(_fixture.CreateContext()),
            new DbSampleSearchDataGateway(_fixture.CreateContext()));

    [Fact]
    public async Task SeedAsync_QuarterPreset_WeeklyVolumeStdDevExceedsFlatBaseline()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc);
        var sut = CreateSut();

        await sut.SeedAsync(
            span: TimeSpan.FromDays(90),
            eventCount: 8_000,
            messageCount: 0,
            nowUtc: now,
            seed: 1234,
            cancellationToken: CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Group by ISO week and compare the coefficient of variation (stddev / mean).
        // The pre-fix baseline (static HourWeights + WeekendDampen only) sits around
        // 0.05 across a quarter; the multi-scale variance takes it above 0.20.
        var weeklyCounts = new List<long>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COUNT(*) AS c
                FROM search_events
                GROUP BY date_trunc('week', occurred_at_utc)
                ORDER BY 1;";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                weeklyCounts.Add(reader.GetInt64(0));
            }
        }

        Assert.True(weeklyCounts.Count >= 10,
            $"Expected roughly a dozen ISO weeks from a 90-day seed; got {weeklyCounts.Count}.");
        var mean = weeklyCounts.Average();
        var variance = weeklyCounts.Sum(c => (c - mean) * (c - mean)) / weeklyCounts.Count;
        var stddev = Math.Sqrt(variance);
        var cov = stddev / mean;
        Assert.True(cov >= 0.20,
            $"Expected weekly coefficient of variation >= 0.20; got {cov:F3} " +
            $"(stddev {stddev:F1}, mean {mean:F1}, weekly counts {string.Join(", ", weeklyCounts)}).");
    }

    [Fact]
    public async Task SeedAsync_QuarterPreset_ContainsAtLeastOneSpikeDay()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc);
        var sut = CreateSut();

        await sut.SeedAsync(
            span: TimeSpan.FromDays(90),
            eventCount: 8_000,
            messageCount: 0,
            nowUtc: now,
            seed: 4242,
            cancellationToken: CancellationToken.None);

        var dailyCounts = await DailyCountsAsync();
        var median = MedianOf(dailyCounts);
        var maxCount = dailyCounts.Max();
        Assert.True(maxCount >= 2.0 * median,
            $"Expected at least one 'spike day' with volume >= 2× median. " +
            $"Median {median:F1}, max {maxCount}.");
    }

    [Fact]
    public async Task SeedAsync_QuarterPreset_ContainsAtLeastOneQuietDay()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc);
        var sut = CreateSut();

        await sut.SeedAsync(
            span: TimeSpan.FromDays(90),
            eventCount: 8_000,
            messageCount: 0,
            nowUtc: now,
            seed: 91919,
            cancellationToken: CancellationToken.None);

        var dailyCounts = await DailyCountsAsync();
        var median = MedianOf(dailyCounts);
        // "Quiet day" — a weekday-ish volume dropping to <= half the median. Weekend
        // days already dip under WeekendDampen, so the anomaly must be visible against
        // the median across ALL days including weekends.
        var minCount = dailyCounts.Min();
        Assert.True(minCount <= 0.5 * median,
            $"Expected at least one 'quiet day' with volume <= 0.5× median. " +
            $"Median {median:F1}, min {minCount}.");
    }

    [Fact]
    public async Task SeedAsync_QuarterPreset_ContainsOutageWindow_WithElevatedLatency()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc);
        var sut = CreateSut();

        await sut.SeedAsync(
            span: TimeSpan.FromDays(90),
            eventCount: 8_000,
            messageCount: 0,
            nowUtc: now,
            seed: 8000,
            cancellationToken: CancellationToken.None);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Overall median latency across the seeded rows.
        double overallMedian;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY latency_ms) FROM search_events;";
            var value = await cmd.ExecuteScalarAsync();
            overallMedian = Convert.ToDouble(value);
        }

        // Look for at least one hour whose MEAN latency is >= 3× overall median AND
        // that hour has >= 3 events (so a single outlier doesn't skew the mean).
        var hasElevatedHour = false;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT AVG(latency_ms) AS mean_ms, COUNT(*) AS n
                FROM search_events
                GROUP BY date_trunc('hour', occurred_at_utc)
                HAVING COUNT(*) >= 3;";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var meanMs = reader.GetDouble(0);
                if (meanMs >= 3.0 * overallMedian)
                {
                    hasElevatedHour = true;
                    break;
                }
            }
        }

        Assert.True(hasElevatedHour,
            $"Expected at least one hour with mean latency >= 3× overall median " +
            $"({overallMedian:F1} ms).");
    }

    // Determinism guard: the same seed + span must produce the identical event
    // timestamps across two independent runs. Without this the "reproducible per
    // session id" invariant would silently regress the moment any code path in the
    // variance layer reads from an out-of-band random source.
    [Fact]
    public async Task SeedAsync_SameSeed_ProducesIdenticalTimestampSequence()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var now = new DateTime(2026, 07, 29, 12, 00, 00, DateTimeKind.Utc);
        var sut1 = CreateSut();
        await sut1.SeedAsync(TimeSpan.FromDays(30), 500, 0, now, seed: 777,
            cancellationToken: CancellationToken.None);
        var firstSequence = await TimestampSequenceAsync();

        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);

        var sut2 = CreateSut();
        await sut2.SeedAsync(TimeSpan.FromDays(30), 500, 0, now, seed: 777,
            cancellationToken: CancellationToken.None);
        var secondSequence = await TimestampSequenceAsync();

        Assert.Equal(firstSequence.Count, secondSequence.Count);
        for (var i = 0; i < firstSequence.Count; i++)
        {
            Assert.Equal(firstSequence[i], secondSequence[i]);
        }
    }

    private async Task<List<long>> DailyCountsAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM search_events
            GROUP BY date_trunc('day', occurred_at_utc)
            ORDER BY 1;";
        var counts = new List<long>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            counts.Add(reader.GetInt64(0));
        }
        return counts;
    }

    private async Task<List<DateTime>> TimestampSequenceAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT occurred_at_utc FROM search_events ORDER BY id;";
        var seq = new List<DateTime>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            seq.Add(reader.GetDateTime(0));
        }
        return seq;
    }

    private static double MedianOf(List<long> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var n = sorted.Count;
        if (n == 0) return 0;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}
