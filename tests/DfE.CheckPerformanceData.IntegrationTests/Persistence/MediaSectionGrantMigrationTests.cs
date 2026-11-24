using DfE.CheckPerformanceData.Application.Admin;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DfE.CheckPerformanceData.IntegrationTests.Persistence;

// A database made before the media library has role grants but no "media" grant. The
// GrantMediaSection migration gives "media" to the editor and admin roles there, once. It runs
// only on a database that already has grants: a new database stays empty, so the start-up seeder
// still writes the full defaults. An admin who later removes "media" from the editor role keeps
// that change, because the migration does not run again and the seeder tops up only the admin.
// Each test gets its own container, because each applies the chain only part-way.
public sealed class MediaSectionGrantMigrationTests : IAsyncLifetime
{
    private const string BeforeGrant = "20261121011831_AddContentBlockMediaUsages";
    private const string Media = "media";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

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

    private async Task<List<string>> SectionsOfAsync(string role)
    {
        await using var ctx = CreateContext();
        return await ctx.AdminSectionAccesses.Where(a => a.RoleName == role)
            .Select(a => a.SectionKey).OrderBy(s => s).ToListAsync();
    }

    // Start-up does the same two steps: apply migrations, then run the seeder.
    private async Task StartAsync()
    {
        await using (var ctx = CreateContext())
            await ctx.Database.MigrateAsync();
        await using (var ctx = CreateContext())
            await new DefaultAdminAccessSeeder(new AdminSectionAccessRepository(ctx)).SeedIfEmptyAsync();
    }

    // The grid as it was before the media library: the editor's CMS set, without media.
    private async Task MakeAnOldDatabaseAsync()
    {
        await using (var ctx = CreateContext())
            await ctx.Database.GetService<IMigrator>().MigrateAsync(BeforeGrant);
        await ExecuteAsync("""
            INSERT INTO "AdminSectionAccesses" ("RoleName", "SectionKey", "CreatedAt", "CreatedBy") VALUES
              ('cypmd_content_access_user', 'content-pages', now(), 'system'),
              ('cypmd_content_access_user', 'content-blocks', now(), 'system'),
              ('cypmd_admin', 'content-pages', now(), 'system'),
              ('cypmd_admin', 'role-settings', now(), 'system');
            """);
    }

    [Fact]
    public async Task AnOldDatabase_GivesMediaToTheEditorAndAdminRoles()
    {
        await MakeAnOldDatabaseAsync();

        await using (var ctx = CreateContext())
            await ctx.Database.MigrateAsync();

        Assert.Contains(Media, await SectionsOfAsync(DefaultAdminAccessSeeder.EditorRole));
        Assert.Contains(Media, await SectionsOfAsync(DefaultAdminAccessSeeder.AdminRole));
        // The editor's other grants are unchanged.
        Assert.Equal(["content-blocks", "content-pages", Media], await SectionsOfAsync(DefaultAdminAccessSeeder.EditorRole));
    }

    [Fact]
    public async Task AGrantRemovedLater_IsNotGivenBackOnTheNextStart()
    {
        await MakeAnOldDatabaseAsync();
        await StartAsync();
        Assert.Contains(Media, await SectionsOfAsync(DefaultAdminAccessSeeder.EditorRole));

        await ExecuteAsync("""DELETE FROM "AdminSectionAccesses" WHERE "RoleName" = 'cypmd_content_access_user' AND "SectionKey" = 'media';""");
        await StartAsync();
        await StartAsync();

        Assert.DoesNotContain(Media, await SectionsOfAsync(DefaultAdminAccessSeeder.EditorRole));
        Assert.Contains("content-pages", await SectionsOfAsync(DefaultAdminAccessSeeder.EditorRole));
    }

    [Fact]
    public async Task ANewDatabase_StaysEmpty_SoTheSeederWritesTheFullDefaults()
    {
        await using (var ctx = CreateContext())
            await ctx.Database.MigrateAsync();
        Assert.Empty(await SectionsOfAsync(DefaultAdminAccessSeeder.EditorRole));
        Assert.Empty(await SectionsOfAsync(DefaultAdminAccessSeeder.AdminRole));

        await StartAsync();

        Assert.Contains(Media, await SectionsOfAsync(DefaultAdminAccessSeeder.EditorRole));
        Assert.Contains("content-staging", await SectionsOfAsync(DefaultAdminAccessSeeder.EditorRole));
        Assert.Equal(DefaultAdminAccessSeeder.AllSections.Count, (await SectionsOfAsync(DefaultAdminAccessSeeder.AdminRole)).Count);
    }
}
