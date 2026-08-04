using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Locks in the search-analytics schema shape at the migration boundary. Three tables land in
// one migration: search_events (parent), search_event_results (child, CASCADE FK), and
// search_messages (free-standing — deliberately NO FK to search_events so retention on the
// two tables can be decoupled). The rescope invariant is that no PII columns exist on
// search_events — a sink DB leak reveals nothing that ties a row to a person.
[Collection(nameof(PostgresCollection))]
[Trait("Category", "W0")]
public sealed class AddSearchAnalyticsMigrationTests
{
    private readonly PostgresFixture _fixture;

    public AddSearchAnalyticsMigrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SearchEvents_TableExists_WithExpectedColumns()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        var columns = await GetColumnsAsync("search_events");

        Assert.Contains("id", columns);
        Assert.Contains("occurred_at_utc", columns);
        Assert.Contains("session_id", columns);
        Assert.Contains("query_raw", columns);
        Assert.Contains("query_normalised", columns);
        Assert.Contains("scope", columns);
        Assert.Contains("results_pages", columns);
        Assert.Contains("results_blocks", columns);
        Assert.Contains("results_total", columns);
        Assert.Contains("zero_results", columns);
        Assert.Contains("latency_ms", columns);
    }

    // The rescope invariant: no identity capture in the sink.
    [Fact]
    public async Task SearchEvents_HasNoPiiColumns()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        var columns = await GetColumnsAsync("search_events");

        Assert.DoesNotContain("user_sub", columns);
        Assert.DoesNotContain("client_ip_hash", columns);
        Assert.DoesNotContain("user_agent_family", columns);
        Assert.DoesNotContain("user_roles", columns);
    }

    [Fact]
    public async Task SearchEventResults_TableExists_WithExpectedColumns()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        var columns = await GetColumnsAsync("search_event_results");

        Assert.Contains("id", columns);
        Assert.Contains("search_event_id", columns);
        Assert.Contains("position", columns);
        Assert.Contains("result_kind", columns);
        Assert.Contains("result_key", columns);
        Assert.Contains("rank", columns);
    }

    [Fact]
    public async Task SearchMessages_TableExists_WithExpectedColumns()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        var columns = await GetColumnsAsync("search_messages");

        Assert.Contains("id", columns);
        Assert.Contains("session_id", columns);
        Assert.Contains("submitted_at_utc", columns);
        Assert.Contains("what_looking_for", columns);
        Assert.Contains("what_got", columns);
        Assert.Contains("email", columns);
        Assert.Contains("is_read", columns);
        Assert.Contains("read_by_admin_sub", columns);
        Assert.Contains("read_at_utc", columns);
        Assert.Contains("is_seeded", columns);
    }

    // The IsSeeded marker lands on all three sink tables in a single follow-up
    // migration. Existing rows default to false so the marker's write-once invariant
    // survives the initial rollout without a data backfill — real rows classified as
    // real data, seeder rows classified as seeded on the next write.
    [Fact]
    public async Task SearchEvents_HasIsSeededColumn()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        var columns = await GetColumnsAsync("search_events");
        Assert.Contains("is_seeded", columns);
    }

    [Fact]
    public async Task SearchEventResults_HasIsSeededColumn()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        var columns = await GetColumnsAsync("search_event_results");
        Assert.Contains("is_seeded", columns);
    }

    [Fact]
    public async Task IsSeeded_DefaultsToFalseOnInsert()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Insert a bare-minimum event row without setting is_seeded — the column
        // default must land the row as real data (is_seeded = false).
        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText = @"
                INSERT INTO search_events
                    (occurred_at_utc, session_id, results_pages, results_blocks, latency_ms)
                VALUES (@t, @s, 1, 0, 10);";
            insert.Parameters.AddWithValue("t", DateTime.UtcNow);
            insert.Parameters.AddWithValue("s", "default-check-session");
            await insert.ExecuteNonQueryAsync();
        }

        await using var select = conn.CreateCommand();
        select.CommandText =
            "SELECT is_seeded FROM search_events WHERE session_id = 'default-check-session' LIMIT 1;";
        var value = await select.ExecuteScalarAsync();
        Assert.NotNull(value);
        Assert.False((bool)value!);

        // Cleanup so subsequent tests in the collection are not polluted.
        await using var cleanup = conn.CreateCommand();
        cleanup.CommandText = "DELETE FROM search_events WHERE session_id = 'default-check-session';";
        await cleanup.ExecuteNonQueryAsync();
    }

    // Support lookup is a plain WHERE session_id = @quoted_value against an indexed column.
    // Zero-result trend charts depend on the partial index over the computed zero_results
    // flag; without it, ZeroResults dashboards seq-scan the whole table.
    [Fact]
    public async Task SearchEvents_HasRequiredIndexes()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        var indexes = await GetIndexesAsync("search_events");

        Assert.Contains("ix_search_events_occurred_at", indexes);
        Assert.Contains("ix_search_events_session_id", indexes);
        Assert.Contains("ix_search_events_query_normalised", indexes);
        Assert.Contains("ix_search_events_zero_results_occurred_at", indexes);
    }

    [Fact]
    public async Task SearchMessages_HasSessionIdIndex()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        var indexes = await GetIndexesAsync("search_messages");

        Assert.Contains("ix_search_messages_session_id", indexes);
    }

    // A per-session support delete cascades child hit rows automatically; no orphan cleanup
    // logic in application code, and no way to leave dangling children behind.
    [Fact]
    public async Task SearchEventResults_HasCascadeForeignKeyToSearchEvents()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT rc.delete_rule
            FROM information_schema.referential_constraints rc
            JOIN information_schema.table_constraints tc
              ON tc.constraint_name = rc.constraint_name
             AND tc.constraint_schema = rc.constraint_schema
            WHERE tc.table_name = 'search_event_results'
              AND tc.constraint_type = 'FOREIGN KEY';";

        var deleteRule = await cmd.ExecuteScalarAsync() as string;
        Assert.Equal("CASCADE", deleteRule);
    }

    // Retention on the two tables must be decoupled — a message may live a year after the
    // events it references have been purged; a FK would prevent that.
    [Fact]
    public async Task SearchMessages_HasNoForeignKeyToSearchEvents()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*)
            FROM information_schema.table_constraints
            WHERE table_name = 'search_messages'
              AND constraint_type = 'FOREIGN KEY';";

        var fkCount = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(0L, fkCount);
    }

    // The results columns exist as regular ints in the entity, but Postgres computes them
    // on insert so the sink writer cannot drift. Both must be marked STORED (persisted)
    // rather than virtual so partial indexes over them can operate.
    [Fact]
    public async Task SearchEvents_ResultsTotalAndZeroResults_AreStoredComputedColumns()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name, is_generated, generation_expression
            FROM information_schema.columns
            WHERE table_name = 'search_events'
              AND column_name IN ('results_total', 'zero_results');";

        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var isGenerated = reader.GetString(1);
            found[name] = isGenerated;
        }

        Assert.Equal("ALWAYS", found["results_total"]);
        Assert.Equal("ALWAYS", found["zero_results"]);
    }

    private async Task<HashSet<string>> GetColumnsAsync(string tableName)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT column_name FROM information_schema.columns WHERE table_name = @t;";
        cmd.Parameters.AddWithValue("t", tableName);

        var columns = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }
        return columns;
    }

    private async Task<HashSet<string>> GetIndexesAsync(string tableName)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT indexname FROM pg_indexes WHERE tablename = @t;";
        cmd.Parameters.AddWithValue("t", tableName);

        var indexes = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }
        return indexes;
    }
}
