using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// Locks in the migration history for the ChangeRequests decision columns.
//
// The decision outcome is persisted in the Outcome/OutcomeKey columns that main introduced. This branch
// reuses those columns rather than carrying its own duplicate DecisionStatus/DecisionOutcomeKey pair, so
// the duplicates must never be created at any point in the chain. Two apply paths have to end at the same
// schema with zero orphan columns:
//   - a fresh database applying the whole chain, and
//   - a Dev/QA database where main's AddChangeRequestOutcome migration (absent from this tree) has already
//     added Outcome/OutcomeKey/MatchedRuleId before this branch's earlier-timestamped migrations run.
// Each test gets its own container because some apply the chain only part-way.
public sealed class ChangeRequestDecisionColumnMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    // The migration immediately before this branch first touches the decision columns.
    private const string BeforeDecisionColumns = "20260608140029_NullifiedPupilDetailsInChangeRequests";

    // main's migration — not present in this tree — that already added Outcome/OutcomeKey/MatchedRuleId
    // and recorded itself in the history on the persistent Dev and QA databases.
    private const string MainOutcomeMigrationId = "20260611090049_AddChangeRequestOutcome";

    private static readonly string[] CanonicalColumns =
        ["Outcome", "OutcomeKey", "MatchedRuleId", "RulesVersion", "DecidedAtUtc"];

    private static readonly string[] ForbiddenDuplicateColumns =
        ["DecisionStatus", "DecisionOutcomeKey"];

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .Options, new FakeCurrentUserService());

    private async Task<bool> ColumnExistsAsync(string table, string column)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT 1 FROM information_schema.columns WHERE table_name = @t AND column_name = @c;";
        cmd.Parameters.AddWithValue("t", table);
        cmd.Parameters.AddWithValue("c", column);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    // The defining property of this branch: the duplicate columns are never created — not transiently,
    // not at any single migration step. A create-then-drop history would leave them present part-way
    // through, which this catches by checking the schema after every migration from the decision-column
    // window onward.
    [Fact]
    public async Task DecisionStatusAndOutcomeKeyDuplicates_AreNeverCreated_AtAnyMigrationStep()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();
        var all = ctx.Database.GetMigrations().ToList();

        await migrator.MigrateAsync(BeforeDecisionColumns);

        for (var i = all.IndexOf(BeforeDecisionColumns) + 1; i < all.Count; i++)
        {
            await migrator.MigrateAsync(all[i]);

            foreach (var forbidden in ForbiddenDuplicateColumns)
            {
                Assert.False(
                    await ColumnExistsAsync("ChangeRequests", forbidden),
                    $"{forbidden} must never exist on ChangeRequests; it was present after applying {all[i]}.");
            }
        }
    }

    // Fresh database, whole chain: the canonical columns exist and the duplicates do not.
    [Fact]
    public async Task FreshDatabase_FinalSchema_HasCanonicalColumns_AndNoDuplicates()
    {
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        foreach (var col in CanonicalColumns)
        {
            Assert.True(await ColumnExistsAsync("ChangeRequests", col), $"expected column {col} to exist.");
        }

        foreach (var forbidden in ForbiddenDuplicateColumns)
        {
            Assert.False(await ColumnExistsAsync("ChangeRequests", forbidden), $"{forbidden} should not exist.");
        }
    }

    // Dev/QA path: main's Outcome/OutcomeKey/MatchedRuleId are already present (and main's migration is
    // recorded in history) before this branch's earlier-timestamped migrations run. EF applies the
    // branch's pending migrations in timestamp order regardless, so they must guard the pre-existing
    // columns and end at the same canonical schema without a "column already exists" collision.
    [Fact]
    public async Task DevQaPath_WhenMainOutcomeColumnsAlreadyApplied_MigratesWithoutCollision()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeDecisionColumns);

        await using (var conn = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                ALTER TABLE "ChangeRequests" ADD COLUMN "Outcome" character varying(20);
                ALTER TABLE "ChangeRequests" ADD COLUMN "OutcomeKey" character varying(100);
                ALTER TABLE "ChangeRequests" ADD COLUMN "MatchedRuleId" character varying(100);
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES (@id, '0.0.0-test');
                """;
            cmd.Parameters.AddWithValue("id", MainOutcomeMigrationId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Must not throw on the pre-existing columns.
        await ctx.Database.MigrateAsync();

        foreach (var col in CanonicalColumns)
        {
            Assert.True(await ColumnExistsAsync("ChangeRequests", col), $"expected column {col} to exist.");
        }

        foreach (var forbidden in ForbiddenDuplicateColumns)
        {
            Assert.False(await ColumnExistsAsync("ChangeRequests", forbidden), $"{forbidden} should not exist.");
        }
    }

    // The other entity that happens to share the C# property name DecisionStatus maps to
    // queue_metrics_events.decision_status. The ChangeRequests reconciliation must not touch it.
    [Fact]
    public async Task QueueMetricEvent_DecisionStatusColumn_SurvivesTheFullChain()
    {
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        Assert.True(
            await ColumnExistsAsync("queue_metrics_events", "decision_status"),
            "queue_metrics_events.decision_status must not be dropped by the ChangeRequests decision-column work.");
    }

    // The model snapshot must match the entity model: if a future change renames or re-adds a decision
    // column without regenerating the snapshot, the next migration scaffolds against a stale baseline.
    [Fact]
    public async Task Model_HasNoPendingChanges_AgainstSnapshot()
    {
        await using var ctx = CreateContext();

        Assert.False(
            ctx.Database.HasPendingModelChanges(),
            "The EF model snapshot is out of sync with the entity model — regenerate PortalDbContextModelSnapshot.");
    }
}
