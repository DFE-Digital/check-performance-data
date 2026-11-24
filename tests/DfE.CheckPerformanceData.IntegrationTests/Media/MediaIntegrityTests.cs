using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Media;

// The database itself refuses media rows that point at nothing. Every reference is a foreign key:
// an image's current version is one of its stored versions, and a usage's page is the page of its
// page version. The application never writes such rows; these checks stop a bug or a hand edit.
[Collection(nameof(PostgresCollection))]
public sealed class MediaIntegrityTests(PostgresFixture postgres) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = postgres.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE media_usages, media_asset_versions, media_assets, ""PageNodes"", ""PageNodeVersions"" RESTART IDENTITY CASCADE;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string Asset(Guid id, int current) =>
        $"INSERT INTO media_assets (id, file_name, slug, current_version, created_at_utc, updated_at_utc) "
        + $"VALUES ('{id}', 'a.png', 'a', {current}, now(), now());";

    private static string Version(Guid id, int version) =>
        $"INSERT INTO media_asset_versions (asset_id, version, blob_key, content_type, size_bytes, sha256, is_animated, uploaded_at_utc) "
        + $"VALUES ('{id}', {version}, '{id}/{version}', 'image/png', 1, '{new string('0', 64)}', false, now());";

    [Fact]
    public async Task AnImageAndItsFirstVersion_CanBeAddedTogether()
    {
        var id = Guid.NewGuid();

        await ExecuteAsync($"BEGIN; {Asset(id, 1)} {Version(id, 1)} COMMIT;");
    }

    [Fact]
    public async Task AnImageWhoseCurrentVersionIsNotStored_IsRefused()
    {
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"BEGIN; {Asset(id, 2)} {Version(id, 1)} COMMIT;"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
        Assert.Equal("fk_media_assets_media_asset_versions_current_version", ex.ConstraintName);
    }

    [Fact]
    public async Task DeletingAnImagesCurrentVersion_IsRefused()
    {
        var id = Guid.NewGuid();
        await ExecuteAsync($"BEGIN; {Asset(id, 1)} {Version(id, 1)} COMMIT;");

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync($"DELETE FROM media_asset_versions WHERE asset_id = '{id}' AND version = 1;"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }

    [Fact]
    public async Task DeletingAnImage_DeletesItsVersions()
    {
        var id = Guid.NewGuid();
        await ExecuteAsync($"BEGIN; {Asset(id, 1)} {Version(id, 1)} COMMIT;");

        await ExecuteAsync($"DELETE FROM media_assets WHERE id = '{id}';");

        await using var ctx = postgres.CreateContext();
        Assert.False(await ctx.MediaAssetVersions.AnyAsync(v => v.AssetId == id));
    }

    // A usage names its page version and that version's page. Another page is refused.
    [Fact]
    public async Task AUsageOnAnotherPageThanItsPageVersion_IsRefused()
    {
        var id = Guid.NewGuid();
        await ExecuteAsync($"BEGIN; {Asset(id, 1)} {Version(id, 1)} COMMIT;");
        Guid first, second;
        await using (var ctx = postgres.CreateContext())
        {
            var service = new PageNodeService(new PageNodeRepository(ctx));
            first = (await service.CreatePageAsync(null, "first", "First", "content", "test")).Id;
            second = (await service.CreatePageAsync(null, "second", "Second", "content", "test")).Id;
        }
        await using (var ctx = postgres.CreateContext())
        {
            var version = await ctx.PageNodeVersions.Where(v => v.PageNodeId == first).Select(v => v.Id).SingleAsync();
            string Insert(Guid page) =>
                "INSERT INTO media_usages (id, asset_id, asset_version, location, page_node_id, page_node_version_id) "
                + $"VALUES ('{Guid.NewGuid()}', '{id}', 1, 'richtext', '{page}', '{version}');";

            await ExecuteAsync(Insert(first));
            var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Insert(second)));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
            Assert.Equal("fk_media_usages_page_node_versions", ex.ConstraintName);
        }
    }
}
