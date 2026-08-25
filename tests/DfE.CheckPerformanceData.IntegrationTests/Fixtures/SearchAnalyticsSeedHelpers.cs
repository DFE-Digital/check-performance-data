using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

// Bulk-seeding helpers for the search-analytics tables. Both use Npgsql's binary COPY
// pipeline (BeginBinaryImport) so a 30k+ row seed completes in well under two seconds —
// the ORM path would take an order of magnitude longer and the retention-timing tests
// would run past their assertion budgets. First use of binary COPY in this repo; the
// column lists are hand-mapped against SearchEventConfiguration + SearchMessageConfiguration
// so a schema-drift regression trips the seeder before it trips the assertions.
internal static class SearchAnalyticsSeedHelpers
{
    // Wipes both tables + their children in one transaction so each test starts from a
    // known-empty state. CASCADE handles search_event_results via the FK declared in
    // SearchEventResultConfiguration.
    public static async Task TruncateAllAsync(PostgresFixture fixture)
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "TRUNCATE search_events, search_messages RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    // Seeds `rowCount` search_events rows distributed evenly across the time window
    // ending at `oldestAt.Add(rowCount * gap)` where gap keeps the distribution dense
    // enough for time-window aggregates. When oldestAt is null, all rows land at
    // "roughly now" for tests that only care about count / value round-trips.
    public static async Task SeedSearchEventsAsync(
        PostgresFixture fixture, int rowCount, DateTime? oldestAt = null)
    {
        if (rowCount <= 0) return;

        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        // Column order MUST match the writer template below. Skip id / results_total /
        // zero_results — id is serial, the other two are STORED computed columns.
        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY search_events (occurred_at_utc, session_id, query_raw, query_normalised, scope, results_pages, results_blocks, latency_ms) FROM STDIN (FORMAT BINARY)");

        var start = oldestAt ?? DateTime.UtcNow.AddMinutes(-rowCount);
        var step = rowCount > 1
            ? TimeSpan.FromMilliseconds(1000.0)
            : TimeSpan.Zero;

        for (var i = 0; i < rowCount; i++)
        {
            var occurredAt = start + TimeSpan.FromTicks(step.Ticks * i);
            await writer.StartRowAsync();
            await writer.WriteAsync(occurredAt, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(
                "seed-session-" + (i % 128).ToString(CultureInfo.InvariantCulture),
                NpgsqlDbType.Text);
            await writer.WriteAsync("seed-query-" + i.ToString(CultureInfo.InvariantCulture),
                NpgsqlDbType.Text);
            await writer.WriteAsync("seed-query-" + i.ToString(CultureInfo.InvariantCulture),
                NpgsqlDbType.Text);
            await writer.WriteNullAsync();
            await writer.WriteAsync(i % 3, NpgsqlDbType.Integer);
            await writer.WriteAsync(i % 5, NpgsqlDbType.Integer);
            await writer.WriteAsync(1 + (i % 25), NpgsqlDbType.Integer);
        }

        await writer.CompleteAsync();
    }

    // Same pattern for search_messages. Skips is_read (defaults to false), email,
    // read_by_admin_sub, read_at_utc — the retention purge doesn't touch these
    // columns and defaults / NULL are fine.
    public static async Task SeedSearchMessagesAsync(
        PostgresFixture fixture, int rowCount, DateTime? oldestAt = null)
    {
        if (rowCount <= 0) return;

        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY search_messages (session_id, submitted_at_utc, what_looking_for) FROM STDIN (FORMAT BINARY)");

        var start = oldestAt ?? DateTime.UtcNow.AddMinutes(-rowCount);
        var step = rowCount > 1
            ? TimeSpan.FromMilliseconds(1000.0)
            : TimeSpan.Zero;

        for (var i = 0; i < rowCount; i++)
        {
            var submittedAt = start + TimeSpan.FromTicks(step.Ticks * i);
            await writer.StartRowAsync();
            await writer.WriteAsync(
                "seed-session-" + (i % 128).ToString(CultureInfo.InvariantCulture),
                NpgsqlDbType.Text);
            await writer.WriteAsync(submittedAt, NpgsqlDbType.TimestampTz);
            await writer.WriteAsync(
                "seed message body " + i.ToString(CultureInfo.InvariantCulture),
                NpgsqlDbType.Text);
        }

        await writer.CompleteAsync();
    }
}
