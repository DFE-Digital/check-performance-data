using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// #324: dataset slots are only reconciled when a window is saved through WindowService, so without
// this backfill every results-enquiry exercise already on a deployed environment would show "no
// ingress files to load" until an admin happened to re-save its window — and nobody could upload
// the results files the enquiry journey reads. Each test gets its own container because they apply
// the migration chain only part-way.
public sealed class ResultsEnquirySlotBackfillTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private const string BeforeSlots = "20260820094844_MoveValidationStampToCheckingExercise";
    private const string SlotMigration = "20260820135315_AddDatasetSourceFile";

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

    private async Task<Guid> InsertWindowWithExercisesAsync(string windowType, params string[] exerciseTypes)
    {
        var windowId = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO "CheckingWindows"
                ("Id", "StartDate", "EndDate", "KeyStage", "CheckingWindowType", "Title",
                 "Published", "IngressFile", "SchemaFile", "IngressFileChecksum", "SchemaFileChecksum")
            VALUES ('{windowId}', '2026-01-01 00:00:00', '2026-06-14 17:00:00', 'Post16', '{windowType}',
                    'Existing window', false, '', '', '', '');
            """);

        for (var i = 0; i < exerciseTypes.Length; i++)
        {
            await ExecuteAsync($"""
                INSERT INTO "CheckingExercises"
                    ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder")
                VALUES (gen_random_uuid(), '{windowId}', '{exerciseTypes[i]}',
                        '2026-01-01 00:00:00', '2026-06-14 17:00:00', {i});
                """);
        }

        return windowId;
    }

    private Task<List<(string Name, string Source, bool Required, int SortOrder)>> SlotsAsync(Guid windowId) =>
        QueryAsync($"""
            SELECT d."Name", d."SourceFile", d."Required", d."SortOrder"
            FROM "CheckingWindowDatasets" d
            JOIN "CheckingExercises" e ON e."Id" = d."CheckingExerciseId"
            WHERE e."CheckingWindowId" = '{windowId}' AND e."ExerciseType" = 'ResultsEnquiry'
            ORDER BY d."SortOrder";
            """, r => (r.GetString(0), r.GetString(1), r.GetBoolean(2), r.GetInt32(3)));

    [Fact]
    public async Task A_16_to_19_results_enquiry_gains_a_slot_per_source_file()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeSlots);
        var windowId = await InsertWindowWithExercisesAsync("Post16", "PupilData", "ResultsEnquiry");

        await migrator.MigrateAsync(SlotMigration);

        var slots = await SlotsAsync(windowId);

        Assert.Equal(
            ["16to19_MAIN", "16to19_LR1", "16to19_LR2", "16to19_Revised", "16to19_Retention"],
            slots.Select(s => s.Name));

        // The slot is named by the tag it stamps, so a file can never be given another file's tag.
        Assert.All(slots, s => Assert.Equal(s.Name, s.Source));

        // Only the main file is required — the rest land weeks apart and one may never land.
        Assert.True(slots[0].Required);
        Assert.All(slots.Skip(1), s => Assert.False(s.Required));
    }

    [Fact]
    public async Task The_pupil_data_exercise_gains_nothing_and_keeps_no_source_tag()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeSlots);
        var windowId = await InsertWindowWithExercisesAsync("Post16", "PupilData", "ResultsEnquiry");

        await migrator.MigrateAsync(SlotMigration);

        var pupilSlots = await QueryAsync($"""
            SELECT d."Id" FROM "CheckingWindowDatasets" d
            JOIN "CheckingExercises" e ON e."Id" = d."CheckingExerciseId"
            WHERE e."CheckingWindowId" = '{windowId}' AND e."ExerciseType" = 'PupilData';
            """, r => r.GetGuid(0));

        // Pupil datasets are created by the window's own type, not by this migration.
        Assert.Empty(pupilSlots);
    }

    [Fact]
    public async Task A_KS4_results_enquiry_gains_the_KS4_source_files()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeSlots);
        var windowId = await InsertWindowWithExercisesAsync("KS4June", "ResultsEnquiry");

        await migrator.MigrateAsync(SlotMigration);

        Assert.Equal(
            ["KS4_MAIN", "KS4_LR1", "KS4_LR2", "KS4_Revised"],
            (await SlotsAsync(windowId)).Select(s => s.Name));
    }

    [Fact]
    public async Task A_KS2_results_enquiry_gains_nothing_because_there_is_no_results_feed()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeSlots);
        var windowId = await InsertWindowWithExercisesAsync("KS2", "ResultsEnquiry");

        await migrator.MigrateAsync(SlotMigration);

        Assert.Empty(await SlotsAsync(windowId));
    }

    [Fact]
    public async Task A_slot_an_admin_has_already_filled_survives_a_rollback()
    {
        // Down removes only the slots nobody uploaded to. A slot holding a file is data an admin
        // put there, and a rollback must not throw it away.
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeSlots);
        var windowId = await InsertWindowWithExercisesAsync("Post16", "ResultsEnquiry");
        await migrator.MigrateAsync(SlotMigration);

        await ExecuteAsync($"""
            UPDATE "CheckingWindowDatasets" SET "IngressFile" = 'main.csv', "SchemaFile" = 'results.json'
            WHERE "Name" = '16to19_MAIN';
            """);

        await migrator.MigrateAsync(BeforeSlots);

        var survivors = await QueryAsync($"""
            SELECT d."Name" FROM "CheckingWindowDatasets" d
            JOIN "CheckingExercises" e ON e."Id" = d."CheckingExerciseId"
            WHERE e."CheckingWindowId" = '{windowId}';
            """, r => r.GetString(0));

        Assert.Equal("16to19_MAIN", Assert.Single(survivors));
    }

    [Fact]
    public async Task Re_running_the_migration_creates_no_second_set_of_slots()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeSlots);
        var windowId = await InsertWindowWithExercisesAsync("Post16", "ResultsEnquiry");
        await migrator.MigrateAsync(SlotMigration);
        await migrator.MigrateAsync(BeforeSlots);
        await migrator.MigrateAsync(SlotMigration);

        Assert.Equal(5, (await SlotsAsync(windowId)).Count);
    }
}
