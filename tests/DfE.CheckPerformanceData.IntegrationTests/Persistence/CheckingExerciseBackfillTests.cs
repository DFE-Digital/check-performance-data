using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// A checking window gains a CheckingExercises child collection, each row on its own dates (#307).
// Every window in the database today runs a single pupil-data activity on the window's own dates,
// so the migration backfills exactly that — one PupilData row per window — and nothing reads the
// table yet. Each test gets its own container because they apply the chain only part-way.
public sealed class CheckingExerciseBackfillTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    // The migration immediately before the exercises table is introduced.
    private const string BeforeExercises = "20260804204028_AddChangeRequestAmendmentType";
    private const string ExercisesMigration = "20260819144921_AddCheckingExercises";

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private PortalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.EnableRetryOnFailure())
            .Options, new FakeCurrentUserService());

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<(string ExerciseType, DateTime StartDate, DateTime EndDate, int SortOrder)>>
        ReadExercisesAsync(Guid windowId)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "ExerciseType", "StartDate", "EndDate", "SortOrder"
            FROM "CheckingExercises" WHERE "CheckingWindowId" = @id ORDER BY "SortOrder";
            """;
        cmd.Parameters.AddWithValue("id", windowId);

        List<(string, DateTime, DateTime, int)> rows = [];
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetDateTime(1), reader.GetDateTime(2), reader.GetInt32(3)));
        }
        return rows;
    }

    private async Task<Guid> InsertLegacyWindowAsync()
    {
        var windowId = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO "CheckingWindows"
                ("Id", "StartDate", "EndDate", "KeyStage", "CheckingWindowType", "Title",
                 "Published", "IngressFile", "SchemaFile", "IngressFileChecksum", "SchemaFileChecksum")
            VALUES ('{windowId}', '2026-01-01 00:00:00', '2026-01-14 17:00:00', 'KS4', 'KS4June',
                    'Legacy window', false, 'pupils.csv', 'pupils-schema.json', 'CSVSUM', 'SCHEMASUM');
            """);
        return windowId;
    }

    [Fact]
    public async Task Backfills_an_existing_window_as_a_single_pupil_data_exercise_on_its_own_dates()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeExercises);
        var windowId = await InsertLegacyWindowAsync();

        await migrator.MigrateAsync(ExercisesMigration);

        var exercise = Assert.Single(await ReadExercisesAsync(windowId));
        Assert.Equal("PupilData", exercise.ExerciseType);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0), exercise.StartDate);
        Assert.Equal(new DateTime(2026, 1, 14, 17, 0, 0), exercise.EndDate);
        Assert.Equal(0, exercise.SortOrder);
    }

    [Fact]
    public async Task Backfill_is_idempotent_so_rerunning_it_adds_nothing()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeExercises);
        var windowId = await InsertLegacyWindowAsync();
        await migrator.MigrateAsync(ExercisesMigration);

        // Re-run the backfill exactly as the migration body does. A database that already has rows
        // (e.g. one where the migration was applied by hand) must not gain duplicates.
        await ExecuteAsync("""
            INSERT INTO "CheckingExercises"
                ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder")
            SELECT gen_random_uuid(), w."Id", 'PupilData', w."StartDate", w."EndDate", 0
            FROM "CheckingWindows" w
            WHERE NOT EXISTS (
                SELECT 1 FROM "CheckingExercises" e
                WHERE e."CheckingWindowId" = w."Id" AND e."ExerciseType" = 'PupilData'
            );
            """);

        Assert.Single(await ReadExercisesAsync(windowId));
    }

    [Fact]
    public async Task A_window_may_hold_several_exercises_but_a_type_only_once()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeExercises);
        var windowId = await InsertLegacyWindowAsync();
        await migrator.MigrateAsync(ExercisesMigration);

        // A second exercise of a different type is fine: this is the 16-19 shape.
        await ExecuteAsync($"""
            INSERT INTO "CheckingExercises"
                ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder")
            VALUES (gen_random_uuid(), '{windowId}', 'ResultsEnquiry',
                    '2026-01-01 00:00:00', '2026-06-30 17:00:00', 1);
            """);
        Assert.Equal(2, (await ReadExercisesAsync(windowId)).Count);

        // A repeat of a type is not.
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            INSERT INTO "CheckingExercises"
                ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder")
            VALUES (gen_random_uuid(), '{windowId}', 'PupilData',
                    '2026-01-01 00:00:00', '2026-01-14 17:00:00', 2);
            """));
        Assert.Equal("23505", duplicate.SqlState);
    }

    [Fact]
    public async Task Deleting_a_window_cascades_to_its_exercises()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeExercises);
        var windowId = await InsertLegacyWindowAsync();
        await migrator.MigrateAsync(ExercisesMigration);
        Assert.Single(await ReadExercisesAsync(windowId));

        await ExecuteAsync($"""DELETE FROM "CheckingWindows" WHERE "Id" = '{windowId}';""");

        Assert.Empty(await ReadExercisesAsync(windowId));
    }

    // If a future change alters the exercise entity without regenerating the snapshot, the next
    // migration scaffolds against a stale baseline.
    [Fact]
    public async Task Model_HasNoPendingChanges_AgainstSnapshot()
    {
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();

        Assert.False(
            ctx.Database.HasPendingModelChanges(),
            "The EF model snapshot is out of sync with the entity model — regenerate PortalDbContextModelSnapshot.");
    }
}
