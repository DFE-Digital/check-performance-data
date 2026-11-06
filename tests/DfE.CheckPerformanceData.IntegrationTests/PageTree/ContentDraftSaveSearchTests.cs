using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

// #475: the content editor's Save button, proved against real Postgres full-text search.
//
// Site search reads the plain-text body of a page's live version. The Save button once wrote an
// empty body, so a page that was saved and then published lost its body from search. These tests
// run the real editor, service and repository: save with no edit, publish, then search for a
// phrase that is in the body and not in the title.
[Collection(nameof(PostgresCollection))]
public sealed class ContentDraftSaveSearchTests(PostgresFixture fixture)
{
    private const string Body = "Return the quokka census by Friday.";

    private readonly PostgresFixture _fixture = fixture;

    private async Task TruncateAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"TRUNCATE ""PageNodeVersions"", ""PageNodes"" RESTART IDENTITY CASCADE;";
        await cmd.ExecuteNonQueryAsync();
    }

    // A content page with one rich text widget, written the way the editor writes it. The page
    // has one version, a draft.
    private async Task<Guid> CreatePageWithBodyAsync()
    {
        await using var ctx = _fixture.CreateContext();
        var pages = new PageNodeService(new PageNodeRepository(ctx));
        var editor = new PageNodeContentEditor(pages, new HtmlRenderingService());

        var page = await pages.CreatePageAsync(null, "draft-save", "Draft save page", "content", "tester");
        await editor.AddWidgetAsync(page.Id, [new TreeStep(0, 0)], "richtext", "tester");
        await editor.UpdateWidgetAsync(page.Id, [new TreeStep(0, 0)],
            new JsonObject { ["html"] = $"<p>{Body}</p>" }, "tester");
        return page.Id;
    }

    private async Task AssertFoundByAPhraseInItsBodyAsync(Guid pageId)
    {
        await using var ctx = _fixture.CreateContext();
        var hits = await new PageNodeRepository(ctx).SearchPagesAsync("\"quokka census\"", null, 10);

        var hit = Assert.Single(hits, h => h.ExcludedBy is null);
        Assert.Equal(pageId, hit.PageId);
        Assert.Contains("quokka census", hit.BodyPlainText);
        Assert.True(hit.RankBody > 0, "The hit must come from the body. The title does not hold the phrase.");
    }

    [Fact]
    public async Task APageSavedWithNoEditAndThenPublished_IsFoundByAPhraseInItsBody()
    {
        await TruncateAsync();
        var pageId = await CreatePageWithBodyAsync();

        await using (var ctx = _fixture.CreateContext())
        {
            var pages = new PageNodeService(new PageNodeRepository(ctx));
            await new PageNodeContentEditor(pages, new HtmlRenderingService()).SaveDraftAsync(pageId, "tester");
            await pages.PublishDraftAsync(pageId, "tester");
        }

        await AssertFoundByAPhraseInItsBodyAsync(pageId);
    }

    [Fact]
    public async Task APublishedPageWhoseOnlyEditIsASave_KeepsItsBodyInEveryVersion_AndIsStillFound()
    {
        await TruncateAsync();
        var pageId = await CreatePageWithBodyAsync();

        await using (var ctx = _fixture.CreateContext())
        {
            var pages = new PageNodeService(new PageNodeRepository(ctx));
            var editor = new PageNodeContentEditor(pages, new HtmlRenderingService());

            await pages.PublishDraftAsync(pageId, "tester");   // version 1 is live; no draft is left
            await editor.SaveDraftAsync(pageId, "tester");     // the Save button: a new draft, version 2
            await pages.PublishDraftAsync(pageId, "tester");   // version 2 is live

            var versions = await pages.GetVersionsAsync(pageId);
            Assert.Equal(2, versions.Count);
            Assert.All(versions, v => Assert.Equal(Body, v.BodyPlainText));
        }

        await AssertFoundByAPhraseInItsBodyAsync(pageId);
    }
}
