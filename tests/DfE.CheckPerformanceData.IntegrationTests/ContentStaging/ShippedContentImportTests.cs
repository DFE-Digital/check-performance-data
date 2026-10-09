using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentStaging;

// The shipped content in the web project's Data/Import folder is imported at start-up in every
// environment, production included, and the guide in it is imported in the one mode that replaces
// content somebody may have edited. Every promise that makes is about what happens to real rows
// over several start-ups — what a second run leaves alone, what a newer guide replaces, what a
// deletion survives — so they are only worth making against a real database, with the real files.
[Collection(nameof(PostgresCollection))]
public sealed class ShippedContentImportTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private static readonly Guid GuideRootId = new("a969611f-33ad-518d-9ce5-dcd82a9b2656");

    private static readonly string ImportFolder = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", "src", "DfE.CheckPerformanceData.Web", "Data", "Import"));

    private static readonly ContentBundle Guide =
        ContentStagingJson.Deserialize(File.ReadAllText(Path.Combine(ImportFolder, "cms-guide.json")))!;

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static Guid PageId(string segment) => Guide.PageNodes.Single(p => p.Segment == segment).Id;

    // A database as start-up finds it: the roots exist, because the root seeder runs first.
    private async Task ResetAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""PageNodes"", ""PageNodeVersions"" RESTART IDENTITY CASCADE;");
        var repo = new PageNodeRepository(ctx);
        await new DefaultPageNodeSeeder(new PageNodeService(repo), repo).SeedAsync();
    }

    private async Task<int> SeedAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var repo = new PageNodeRepository(ctx);
        var staging = new ContentStagingService(repo, new ContentBlockRepository(ctx), new HtmlRenderingService());
        var summary = await new ManifestContentImporter(repo, staging, NullLogger<ManifestContentImporter>.Instance)
            .RunAsync(ImportFolder, "Production");
        Assert.Equal(0, summary.FilesFailed);
        return summary.PagesChanged;
    }

    // Stands in for the passing of time: makes a page look as though it was last changed before
    // the shipped guide was issued, which is the state an environment is in when a release brings
    // a newer guide.
    private async Task MakeOlderThanTheGuideAsync(Guid pageId, string content)
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            @"UPDATE ""PageNodeVersions"" SET ""Content"" = {0}, ""UpdatedDate"" = {1} WHERE ""PageNodeId"" = {2};",
            content, Guide.ExportedAtUtc!.Value.AddDays(-1), pageId);
    }

    [Fact]
    public async Task SeedAsync_OnAFreshEnvironment_CreatesEveryGuidePagePublishedUnderHelp()
    {
        await ResetAsync();

        var touched = await SeedAsync();

        Assert.Equal(Guide.PageNodes.Count, touched);

        await using var conn = await OpenAsync();
        var published = await ScalarLongAsync(conn, @"
            SELECT COUNT(DISTINCT n.""Id"") FROM ""PageNodes"" n
            JOIN ""PageNodeVersions"" v ON v.""PageNodeId"" = n.""Id""
            WHERE n.""Path"" LIKE 'help/how-to-use-the-cms%' AND v.""IsCurrent"";");
        Assert.Equal(Guide.PageNodes.Count, published);

        var rootParent = await ScalarAsync<Guid>(conn,
            @"SELECT ""ParentId"" FROM ""PageNodes"" WHERE ""Id"" = @id;", ("id", GuideRootId));
        Assert.Equal(DefaultPageNodeRoots.All.Single(r => r.Segment == "help").Id, rootParent);
    }

    // The property that makes it safe to run on every start-up: once the guide is in place, the
    // next start writes nothing — no new versions, no changed rows.
    [Fact]
    public async Task SeedAsync_ASecondTime_TouchesNothing()
    {
        await ResetAsync();
        await SeedAsync();

        string Snapshot(NpgsqlConnection conn) => ScalarAsync<string>(conn, @"
            SELECT string_agg(v.""Id""::text || v.""UpdatedDate""::text, ',' ORDER BY v.""Id"")
            FROM ""PageNodeVersions"" v;").GetAwaiter().GetResult();

        string before;
        await using (var conn = await OpenAsync()) before = Snapshot(conn);

        var touched = await SeedAsync();

        Assert.Equal(0, touched);
        await using var after = await OpenAsync();
        Assert.Equal(before, Snapshot(after));
    }

    // How a release delivers a newer guide to an environment that already has an older one.
    [Fact]
    public async Task SeedAsync_ReplacesAPage_LastChangedBeforeTheGuideWasIssued()
    {
        await ResetAsync();
        await SeedAsync();
        var page = PageId("for-editors");
        await MakeOlderThanTheGuideAsync(page, "[]");

        var touched = await SeedAsync();

        Assert.Equal(1, touched);
        await using var conn = await OpenAsync();
        var content = await ScalarAsync<string>(conn,
            @"SELECT ""Content"" FROM ""PageNodeVersions"" WHERE ""PageNodeId"" = @id AND ""IsCurrent"";", ("id", page));
        Assert.Equal(Guide.PageNodes.Single(p => p.Id == page).Versions.Single().Content, content);
    }

    // A correction made in one environment survives a restart. It is replaced only when a guide
    // issued after the edit arrives.
    [Fact]
    public async Task SeedAsync_KeepsAnEdit_MadeAfterTheGuideWasIssued()
    {
        await ResetAsync();
        await SeedAsync();
        var page = PageId("for-editors");
        const string edited = """[{"kind":"widget","type":"richtext","props":{"html":"<p>Local correction</p>"}}]""";
        await using (var ctx = _fixture.CreateContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                @"UPDATE ""PageNodeVersions"" SET ""Content"" = {0}, ""UpdatedDate"" = {1} WHERE ""PageNodeId"" = {2};",
                edited, DateTime.UtcNow, page);
        }

        var touched = await SeedAsync();

        Assert.Equal(0, touched);
        await using var conn = await OpenAsync();
        var content = await ScalarAsync<string>(conn,
            @"SELECT ""Content"" FROM ""PageNodeVersions"" WHERE ""PageNodeId"" = @id AND ""IsCurrent"";", ("id", page));
        Assert.Equal(edited, content);
    }

    // Deleting a guide page is a decision, and a restart does not undo it. The deleted row keeps
    // its identity, so without care the importer would try to create the page a second time and
    // report a failure on every start-up.
    [Fact]
    public async Task SeedAsync_LeavesADeletedPageDeleted()
    {
        await ResetAsync();
        await SeedAsync();
        var page = PageId("glossary");
        await using (var ctx = _fixture.CreateContext())
        {
            await new PageNodeRepository(ctx).SoftDeleteAsync(page, "an-administrator");
        }

        var touched = await SeedAsync();

        Assert.Equal(0, touched);
        await using var conn = await OpenAsync();
        var live = await ScalarLongAsync(conn,
            @"SELECT COUNT(*) FROM ""PageNodes"" WHERE ""Id"" = @id AND ""DeletedDate"" IS NULL;", ("id", page));
        Assert.Equal(0L, live);
        var total = await ScalarLongAsync(conn,
            @"SELECT COUNT(*) FROM ""PageNodes"" WHERE ""Path"" LIKE 'help/how-to-use-the-cms%';");
        Assert.Equal(Guide.PageNodes.Count, total);
    }

    // The guide is not the only thing under /help. Nothing else there is the import's to change.
    [Fact]
    public async Task SeedAsync_LeavesEverythingOutsideTheGuideAlone()
    {
        await ResetAsync();
        string Others(NpgsqlConnection conn) => ScalarAsync<string>(conn, @"
            SELECT string_agg(n.""Path"" || n.""UpdatedDate""::text, ',' ORDER BY n.""Path"")
            FROM ""PageNodes"" n WHERE n.""Path"" NOT LIKE 'help/how-to-use-the-cms%';").GetAwaiter().GetResult();

        string before;
        await using (var conn = await OpenAsync()) before = Others(conn);

        await SeedAsync();

        await using var after = await OpenAsync();
        Assert.Equal(before, Others(after));
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task<long> ScalarLongAsync(
        NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt64(await ScalarAsync<object>(conn, sql, parameters));

    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}
