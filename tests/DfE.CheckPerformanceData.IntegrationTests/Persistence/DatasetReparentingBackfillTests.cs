using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// A dataset is an input to one exercise, not to the window (#314). Every dataset row in the
// database today serves the window's single pupil-data activity, so the migration points each one
// at the PupilData exercise #313 backfilled for its window. The legacy CheckingWindowId column
// survives the migration so the release can be rolled back. Each test gets its own container
// because they apply the chain only part-way.
public sealed class DatasetReparentingBackfillTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private const string BeforeReparenting = "20260819144921_AddCheckingExercises";
    private const string ReparentingMigration = "20260819164322_ReparentDatasetsOntoCheckingExercise";

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

    private async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> read)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        List<T> rows = [];
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(read(reader));
        }
        return rows;
    }

    private async Task<Guid> InsertWindowWithDatasetsAsync(params string[] datasetNames)
    {
        var windowId = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO "CheckingWindows"
                ("Id", "StartDate", "EndDate", "KeyStage", "CheckingWindowType", "Title",
                 "Published", "IngressFile", "SchemaFile", "IngressFileChecksum", "SchemaFileChecksum")
            VALUES ('{windowId}', '2026-01-01 00:00:00', '2026-01-14 17:00:00', 'KS4', 'KS4June',
                    'Legacy window', false, 'pupils.csv', 'pupils-schema.json', 'CSVSUM', 'SCHEMASUM');
            """);

        for (var i = 0; i < datasetNames.Length; i++)
        {
            await ExecuteAsync($"""
                INSERT INTO "CheckingWindowDatasets"
                    ("Id", "CheckingWindowId", "Name", "IngressFile", "IngressFileChecksum",
                     "SchemaFile", "SchemaFileChecksum", "SortOrder")
                VALUES (gen_random_uuid(), '{windowId}', '{datasetNames[i]}', '{datasetNames[i]}.csv',
                        'CSVSUM', '{datasetNames[i]}-schema.json', 'SCHEMASUM', {i});
                """);
        }

        return windowId;
    }

    [Fact]
    public async Task Every_existing_dataset_points_at_its_windows_pupil_data_exercise()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeReparenting);
        var windowId = await InsertWindowWithDatasetsAsync("included", "nonincluded");

        await migrator.MigrateAsync(ReparentingMigration);

        var rows = await QueryAsync($"""
            SELECT d."Name", e."ExerciseType"
            FROM "CheckingWindowDatasets" d
            JOIN "CheckingExercises" e ON e."Id" = d."CheckingExerciseId"
            WHERE e."CheckingWindowId" = '{windowId}'
            ORDER BY d."SortOrder";
            """, r => (Name: r.GetString(0), ExerciseType: r.GetString(1)));

        Assert.Equal(["included", "nonincluded"], rows.Select(r => r.Name));
        Assert.All(rows, r => Assert.Equal("PupilData", r.ExerciseType));
    }

    // Rollback safety: the previous release reads datasets through this column, so the migration
    // must leave both the column and its values alone. A follow-up ticket drops it.
    [Fact]
    public async Task The_legacy_window_id_column_keeps_its_values()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeReparenting);
        var windowId = await InsertWindowWithDatasetsAsync("pupils");

        await migrator.MigrateAsync(ReparentingMigration);

        var legacy = await QueryAsync(
            """SELECT "CheckingWindowId" FROM "CheckingWindowDatasets";""",
            r => r.GetGuid(0));

        Assert.Equal(windowId, Assert.Single(legacy));
    }

    [Fact]
    public async Task A_window_whose_datasets_were_never_loaded_migrates_cleanly()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeReparenting);
        await InsertWindowWithDatasetsAsync();

        await migrator.MigrateAsync(ReparentingMigration);

        Assert.Empty(await QueryAsync(
            """SELECT "Id" FROM "CheckingWindowDatasets";""", r => r.GetGuid(0)));
    }

    [Fact]
    public async Task Deleting_an_exercise_cascades_to_its_datasets()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeReparenting);
        var windowId = await InsertWindowWithDatasetsAsync("pupils");
        await migrator.MigrateAsync(ReparentingMigration);

        await ExecuteAsync($"""DELETE FROM "CheckingExercises" WHERE "CheckingWindowId" = '{windowId}';""");

        Assert.Empty(await QueryAsync(
            """SELECT "Id" FROM "CheckingWindowDatasets";""", r => r.GetGuid(0)));
    }

    // Two exercises of one window may each hold a dataset of the same name — the uniqueness that
    // used to be per window is now per exercise.
    [Fact]
    public async Task A_name_may_repeat_across_exercises_but_not_within_one()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeReparenting);
        var windowId = await InsertWindowWithDatasetsAsync("pupils");
        await migrator.MigrateAsync(ReparentingMigration);

        var secondExerciseId = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO "CheckingExercises"
                ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder")
            VALUES ('{secondExerciseId}', '{windowId}', 'ResultsEnquiry',
                    '2026-01-01 00:00:00', '2026-06-30 17:00:00', 1);
            """);

        await ExecuteAsync($"""
            INSERT INTO "CheckingWindowDatasets"
                ("Id", "CheckingExerciseId", "CheckingWindowId", "Name", "IngressFile",
                 "IngressFileChecksum", "SchemaFile", "SchemaFileChecksum", "SortOrder")
            VALUES (gen_random_uuid(), '{secondExerciseId}', '{windowId}', 'pupils', 'r.csv',
                    'CSVSUM', 'r-schema.json', 'SCHEMASUM', 0);
            """);

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"""
            INSERT INTO "CheckingWindowDatasets"
                ("Id", "CheckingExerciseId", "CheckingWindowId", "Name", "IngressFile",
                 "IngressFileChecksum", "SchemaFile", "SchemaFileChecksum", "SortOrder")
            VALUES (gen_random_uuid(), '{secondExerciseId}', '{windowId}', 'pupils', 'r2.csv',
                    'CSVSUM', 'r2-schema.json', 'SCHEMASUM', 1);
            """));
        Assert.Equal("23505", duplicate.SqlState);
    }

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
