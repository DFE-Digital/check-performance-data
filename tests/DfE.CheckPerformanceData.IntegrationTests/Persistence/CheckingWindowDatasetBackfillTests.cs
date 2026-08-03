using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// AddCheckingWindowDatasets moves a window's single IngressFile/SchemaFile pair into a child
// table, because a 16-19 window ingests two supplier files. Windows created before the migration
// must keep validating unchanged, so the migration backfills each one as a single "pupils"
// dataset carrying the files it already had. Each test gets its own container because they apply
// the chain only part-way.
public sealed class CheckingWindowDatasetBackfillTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    // The migration immediately before the datasets table is introduced.
    private const string BeforeDatasets = "20260721132851_RenameWikiPageLengthSettingKey";
    private const string DatasetsMigration = "20260728143037_AddCheckingWindowDatasets";

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

    private async Task<List<(string Name, string IngressFile, string SchemaFile, bool? Included, int SortOrder)>>
        ReadDatasetsAsync(Guid windowId)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Name", "IngressFile", "SchemaFile", "Included", "SortOrder"
            FROM "CheckingWindowDatasets" WHERE "CheckingWindowId" = @id ORDER BY "SortOrder";
            """;
        cmd.Parameters.AddWithValue("id", windowId);

        List<(string, string, string, bool?, int)> rows = [];
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetBoolean(3),
                reader.GetInt32(4)));
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
    public async Task Backfills_an_existing_window_as_a_single_pupils_dataset_carrying_its_files()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeDatasets);
        var windowId = await InsertLegacyWindowAsync();

        await migrator.MigrateAsync(DatasetsMigration);

        var dataset = Assert.Single(await ReadDatasetsAsync(windowId));
        Assert.Equal("pupils", dataset.Name);
        Assert.Equal("pupils.csv", dataset.IngressFile);
        Assert.Equal("pupils-schema.json", dataset.SchemaFile);
        // Null inclusion: a KS4 record carries its own P_INCL, so nothing is stamped at ingest.
        Assert.Null(dataset.Included);
        Assert.Equal(0, dataset.SortOrder);
    }

    [Fact]
    public async Task Backfill_is_idempotent_so_rerunning_it_adds_nothing()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeDatasets);
        var windowId = await InsertLegacyWindowAsync();
        await migrator.MigrateAsync(DatasetsMigration);

        // Re-run the backfill exactly as the migration body does. A database that already has
        // rows (e.g. one where the migration was applied by hand) must not gain duplicates.
        await ExecuteAsync("""
            INSERT INTO "CheckingWindowDatasets"
                ("Id", "CheckingWindowId", "Name", "IngressFile", "IngressFileChecksum",
                 "SchemaFile", "SchemaFileChecksum", "Included", "SortOrder")
            SELECT gen_random_uuid(), w."Id", 'pupils',
                   COALESCE(w."IngressFile", ''), COALESCE(w."IngressFileChecksum", ''),
                   COALESCE(w."SchemaFile", ''), COALESCE(w."SchemaFileChecksum", ''),
                   NULL, 0
            FROM "CheckingWindows" w
            WHERE NOT EXISTS (
                SELECT 1 FROM "CheckingWindowDatasets" d WHERE d."CheckingWindowId" = w."Id"
            );
            """);

        Assert.Single(await ReadDatasetsAsync(windowId));
    }

    [Fact]
    public async Task Deleting_a_window_cascades_to_its_datasets()
    {
        await using var ctx = CreateContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        await migrator.MigrateAsync(BeforeDatasets);
        var windowId = await InsertLegacyWindowAsync();
        await migrator.MigrateAsync(DatasetsMigration);
        Assert.Single(await ReadDatasetsAsync(windowId));

        await ExecuteAsync($"""DELETE FROM "CheckingWindows" WHERE "Id" = '{windowId}';""");

        Assert.Empty(await ReadDatasetsAsync(windowId));
    }

    // If a future change alters the datasets entity without regenerating the snapshot, the next
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
