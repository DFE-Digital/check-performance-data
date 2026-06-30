using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentPages;

// A content page keeps an always-editable draft tree plus a linear history of published versions.
// Draft edits autosave with no version churn; a page-level publish snapshots the draft as the next
// integer version and marks it published; restoring an old version re-publishes its snapshot as a
// new version (history only ever grows). Exercised against real Postgres so the jsonb storage and
// the unique-slug constraint are covered too.
[Collection(nameof(PostgresCollection))]
public sealed class ContentPageVersioningTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""ContentPageVersions"", ""ContentPages"" RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    // Fresh context per service so each call commits and the next read sees DB state, not a cache.
    private ContentPageService NewService() =>
        new(new ContentPageRepository(_fixture.CreateContext()));

    private const string Tree1 = """[{"kind":"widget","type":"heading","anchor":"key-dates","props":{"level":2,"text":"Key dates"}}]""";
    private const string Tree2 = """[{"kind":"widget","type":"heading","anchor":"get-support","props":{"level":2,"text":"Get support"}}]""";

    // jsonb storage is canonical (re-spaced, keys reordered), so content equality is semantic, not
    // byte-exact — what matters is the tree round-trips, which is all the renderer ever reads.
    private static void AssertJsonEquivalent(string expected, string? actual) =>
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual ?? "null")),
            $"JSON not equivalent.\nExpected: {expected}\nActual:   {actual}");

    [Fact]
    public async Task Create_StartsWithEmptyDraft_AndNoPublishedVersion()
    {
        await TruncateAsync();

        var created = await NewService().CreateAsync("ks4", "KS4 checking", null, "nav-content");

        Assert.Equal("ks4", created.Slug);
        Assert.Null(created.PublishedVersionNumber);

        var reloaded = await NewService().GetBySlugAsync("ks4");
        Assert.NotNull(reloaded);
        Assert.Equal("[]", reloaded!.DraftContent);
        Assert.Null(reloaded.PublishedVersionNumber);
    }

    [Fact]
    public async Task SaveDraft_UpdatesDraft_WithoutCreatingAVersion()
    {
        await TruncateAsync();
        await NewService().CreateAsync("ks4", "KS4", null, "nav-content");

        await NewService().SaveDraftAsync("ks4", Tree1);

        var page = await NewService().GetBySlugAsync("ks4");
        AssertJsonEquivalent(Tree1, page!.DraftContent);
        Assert.Null(page.PublishedVersionNumber);
        Assert.Null(await NewService().GetPublishedContentAsync("ks4"));
    }

    [Fact]
    public async Task Publish_SnapshotsDraft_AsVersionOne_AndMarksPublished()
    {
        await TruncateAsync();
        await NewService().CreateAsync("ks4", "KS4", null, "nav-content");
        await NewService().SaveDraftAsync("ks4", Tree1);

        var version = await NewService().PublishAsync("ks4");

        Assert.Equal(1, version);
        var page = await NewService().GetBySlugAsync("ks4");
        Assert.Equal(1, page!.PublishedVersionNumber);
        AssertJsonEquivalent(Tree1, await NewService().GetPublishedContentAsync("ks4"));
    }

    [Fact]
    public async Task Publish_Twice_IncrementsTheVersionAndAdvancesPublished()
    {
        await TruncateAsync();
        await NewService().CreateAsync("ks4", "KS4", null, "nav-content");
        await NewService().SaveDraftAsync("ks4", Tree1);
        await NewService().PublishAsync("ks4");

        await NewService().SaveDraftAsync("ks4", Tree2);
        var second = await NewService().PublishAsync("ks4");

        Assert.Equal(2, second);
        var page = await NewService().GetBySlugAsync("ks4");
        Assert.Equal(2, page!.PublishedVersionNumber);
        AssertJsonEquivalent(Tree2, await NewService().GetPublishedContentAsync("ks4"));
    }

    [Fact]
    public async Task Restore_CopiesSnapshotToDraft_AndRepublishesAsANewVersion()
    {
        await TruncateAsync();
        await NewService().CreateAsync("ks4", "KS4", null, "nav-content");
        await NewService().SaveDraftAsync("ks4", Tree1);
        await NewService().PublishAsync("ks4");           // v1 = Tree1
        await NewService().SaveDraftAsync("ks4", Tree2);
        await NewService().PublishAsync("ks4");           // v2 = Tree2

        var restored = await NewService().RestoreVersionAsync("ks4", 1);

        Assert.Equal(3, restored);                        // history only grows
        var page = await NewService().GetBySlugAsync("ks4");
        AssertJsonEquivalent(Tree1, page!.DraftContent);
        Assert.Equal(3, page.PublishedVersionNumber);
        AssertJsonEquivalent(Tree1, await NewService().GetPublishedContentAsync("ks4"));
        Assert.Equal(3, (await NewService().GetVersionsAsync("ks4")).Count);
    }

    [Fact]
    public async Task Create_DuplicateSlug_IsRejected()
    {
        await TruncateAsync();
        await NewService().CreateAsync("ks4", "KS4", null, "nav-content");

        await Assert.ThrowsAsync<DbUpdateException>(
            () => NewService().CreateAsync("ks4", "KS4 again", null, "nav-content"));
    }

    [Fact]
    public async Task DraftContent_RoundTripsThroughJsonbAsAValidTree()
    {
        await TruncateAsync();
        await NewService().CreateAsync("ks4", "KS4", null, "nav-content");

        IReadOnlyList<ContentNode> tree =
        [
            new RegionNode
            {
                Layout = RegionLayout.TwoThirdsOneThird,
                Columns =
                [
                    [new WidgetNode { Type = "heading", Anchor = "overview", Props = new() { ["level"] = 2, ["text"] = "Overview" } }],
                    [new WidgetNode { Type = "richtext", Props = new() { ["html"] = "<p>Aside</p>" } }]
                ]
            }
        ];
        await NewService().SaveDraftAsync("ks4", ContentPageJson.Serialize(tree));

        var page = await NewService().GetBySlugAsync("ks4");
        var roundTripped = ContentPageJson.Deserialize(page!.DraftContent);

        var region = Assert.IsType<RegionNode>(Assert.Single(roundTripped!));
        Assert.Equal(RegionLayout.TwoThirdsOneThird, region.Layout);
        Assert.Equal("overview", Assert.IsType<WidgetNode>(region.Columns[0][0]).Anchor);
    }
}
