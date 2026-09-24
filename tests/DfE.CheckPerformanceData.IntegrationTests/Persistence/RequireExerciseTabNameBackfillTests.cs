using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// A null tab name used to exempt a row from IsEnabled and the visibility dates. The exemption is
// gone and the column is NOT NULL, so the migration must name and enable those rows, or schools
// lose the data they can see today. Each test gets its own container because they apply the
// migration chain only part-way.
public sealed class RequireExerciseTabNameBackfillTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private const string Before = "20260924090623_AddExerciseLayoutAndJourneySlots";
    private const string Migration = "20260924153339_RequireExerciseTabName";

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

    private async Task<(string TabName, bool IsEnabled)> ExerciseAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""SELECT "TabName", "IsEnabled" FROM "CheckingExercises" WHERE "Id" = '{id}';""";
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetBoolean(1));
    }

    private async Task<Guid> InsertWindowAsync(string windowType)
    {
        var windowId = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO "CheckingWindows"
                ("Id", "StartDate", "EndDate", "KeyStage", "CheckingWindowType", "Title",
                 "Published", "IngressFile", "SchemaFile", "IngressFileChecksum", "SchemaFileChecksum")
            VALUES ('{windowId}', '2026-01-01 00:00:00', '2026-06-14 17:00:00', 'KS4', '{windowType}',
                    'Existing window', false, '', '', '', '');
            """);
        return windowId;
    }

    private async Task<Guid> InsertExerciseAsync(Guid windowId, string exerciseType, string? tabName = null,
        bool enabled = false, bool usesExerciseStorage = false)
    {
        var id = Guid.NewGuid();
        var tab = tabName is null ? "NULL" : $"'{tabName}'";
        await ExecuteAsync($"""
            INSERT INTO "CheckingExercises"
                ("Id", "CheckingWindowId", "ExerciseType", "StartDate", "EndDate", "SortOrder",
                 "TabName", "IsEnabled", "UsesExerciseStorage")
            VALUES ('{id}', '{windowId}', '{exerciseType}', '2026-01-01 00:00:00', '2026-06-14 17:00:00', 0,
                    {tab}, {enabled}, {usesExerciseStorage});
            """);
        return id;
    }

    [Fact]
    public async Task Rows_with_no_tab_name_get_their_kinds_default_and_are_enabled()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);

        var ks4 = await InsertWindowAsync("KS4June");
        var ks4Pupils = await InsertExerciseAsync(ks4, "PupilData");
        var post16 = await InsertWindowAsync("Post16");
        var post16Pupils = await InsertExerciseAsync(post16, "PupilData");
        var post16Results = await InsertExerciseAsync(post16, "ResultsEnquiry");

        await migrator.MigrateAsync(Migration);

        Assert.Equal(("Pupils", true), await ExerciseAsync(ks4Pupils));
        Assert.Equal(("Students", true), await ExerciseAsync(post16Pupils));
        Assert.Equal(("Results", true), await ExerciseAsync(post16Results));
    }

    [Fact]
    public async Task A_named_row_is_left_alone_and_a_clashing_row_stays_disabled()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);

        var window = await InsertWindowAsync("KS4June");
        var named = await InsertExerciseAsync(window, "PupilData", tabName: "Pupils 2026", enabled: true,
            usesExerciseStorage: true);
        var disabledNamed = await InsertExerciseAsync(window, "ResultsEnquiry", tabName: "Old results",
            usesExerciseStorage: true);
        var unnamed = await InsertExerciseAsync(window, "PupilData");

        await migrator.MigrateAsync(Migration);

        Assert.Equal(("Pupils 2026", true), await ExerciseAsync(named));
        Assert.Equal(("Old results", false), await ExerciseAsync(disabledNamed));
        // Enabling it would give the window two live pupil-data exercises.
        Assert.Equal(("Pupils", false), await ExerciseAsync(unnamed));
    }

    [Fact]
    public async Task The_column_refuses_a_null_tab_name_afterwards()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(Before);
        var window = await InsertWindowAsync("KS4June");

        await migrator.MigrateAsync(Migration);

        await Assert.ThrowsAsync<PostgresException>(() => InsertExerciseAsync(window, "PupilData"));
    }
}
