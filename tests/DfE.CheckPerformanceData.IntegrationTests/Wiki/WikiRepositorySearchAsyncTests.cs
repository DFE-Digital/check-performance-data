using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Wiki;

[Collection(nameof(PostgresCollection))]
public sealed class WikiRepositorySearchAsyncTests(PostgresFixture fixture)
{
    private readonly PostgresFixture _fixture = fixture;

    private WikiRepository CreateRepo(out DfE.CheckPerformanceData.Persistence.Contexts.PortalDbContext ctx)
    {
        ctx = _fixture.CreateContext();
        return new WikiRepository(ctx, new FakeCurrentUserService());
    }

    private static WikiPage NewPage(string title, string slug, string bodyPlainText, int? parentId = null, bool isDeleted = false)
    {
        var now = DateTime.UtcNow;
        return new WikiPage
        {
            Title = title,
            Slug = slug,
            Content = $"<p>{bodyPlainText}</p>",
            BodyPlainText = bodyPlainText,
            ParentId = parentId,
            CreatedAt = now,
            UpdatedAt = now,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? now : (DateTime?)null,
        };
    }

    // --- Schema ---

    [Fact]
    public async Task Migration_CreatesGinIndexOnSearchVector()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT indexdef FROM pg_indexes
                            WHERE tablename = 'WikiPages'
                              AND indexname = 'IX_WikiPages_SearchVector';";
        var indexdef = (string?)await cmd.ExecuteScalarAsync();

        Assert.NotNull(indexdef);
        Assert.Contains("USING gin", indexdef);
        Assert.Contains("SearchVector", indexdef);
    }

    [Fact]
    public async Task Migration_CreatesStoredGeneratedSearchVectorColumn()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Cast attgenerated (Postgres "char" type, 1 byte) to text so Npgsql returns a string.
        cmd.CommandText = @"SELECT attgenerated::text FROM pg_attribute
                            WHERE attrelid = '""WikiPages""'::regclass
                              AND attname = 'SearchVector';";
        var attgenerated = (string?)await cmd.ExecuteScalarAsync();

        // 's' = stored generated column
        Assert.Equal("s", attgenerated);
    }

    // --- Matches ---

    [Fact]
    public async Task Matches_ReturnsTitleSlugAndSnippet()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        ctx.WikiPages.Add(NewPage("Publishing guidelines", "publishing-guidelines",
            "How to publish content to real schools."));
        await ctx.SaveChangesAsync();

        var (items, total) = await repo.SearchAsync("publish", skip: 0, take: 20);

        Assert.Equal(1, total);
        var row = Assert.Single(items);
        Assert.Equal("Publishing guidelines", row.Title);
        Assert.Equal("publishing-guidelines", row.Slug);
        Assert.False(string.IsNullOrEmpty(row.SnippetHtml));
    }

    [Fact]
    public async Task Snippet_WrapsMatchesWithMark()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        ctx.WikiPages.Add(NewPage("Guidance", "guidance",
            "Authors should publish content to the schools portal each term."));
        await ctx.SaveChangesAsync();

        var (items, _) = await repo.SearchAsync("publish", 0, 20);

        var row = Assert.Single(items);
        Assert.Contains("<mark>", row.SnippetHtml);
        Assert.Contains("</mark>", row.SnippetHtml);
    }

    [Fact]
    public async Task TitleMatch_OutranksBody()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        // Both match "workflow" — one in title (weight A), one only in body (weight B).
        ctx.WikiPages.Add(NewPage("Workflow overview", "workflow-overview",
            "High-level description of document handling."));
        ctx.WikiPages.Add(NewPage("Document handling", "document-handling",
            "Explains the workflow for review and release."));
        await ctx.SaveChangesAsync();

        var (items, total) = await repo.SearchAsync("workflow", 0, 20);

        Assert.Equal(2, total);
        Assert.Equal("workflow-overview", items[0].Slug); // title-match ranks higher
        Assert.Equal("document-handling", items[1].Slug);
    }

    [Fact]
    public async Task Stemming_MatchesInflectedForms()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        ctx.WikiPages.Add(NewPage("Release notes", "release-notes",
            "Authors publish updates weekly."));
        await ctx.SaveChangesAsync();

        // Query "publishing" should stem to "publish" under the english config and match the body.
        var (items, total) = await repo.SearchAsync("publishing", 0, 20);

        Assert.Equal(1, total);
        Assert.Equal("release-notes", items[0].Slug);
    }

    // --- Access control ---

    [Fact]
    public async Task ExcludesSoftDeleted()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        ctx.WikiPages.Add(NewPage("Live publish", "live-publish",
            "Current guidance about publishing."));
        ctx.WikiPages.Add(NewPage("Retired publish", "retired-publish",
            "Old draft about publishing.", isDeleted: true));
        await ctx.SaveChangesAsync();

        var (items, total) = await repo.SearchAsync("publish", 0, 20);

        Assert.Equal(1, total);
        var row = Assert.Single(items);
        Assert.Equal("live-publish", row.Slug);
        Assert.DoesNotContain(items, i => i.Slug == "retired-publish");
    }

    // --- Query robustness ---

    [Fact]
    public async Task HandlesUnbalancedQuotes()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        ctx.WikiPages.Add(NewPage("Any page", "any-page", "content here"));
        await ctx.SaveChangesAsync();

        // Unbalanced quote + stray operator — websearch_to_tsquery must tolerate.
        var ex = await Record.ExceptionAsync(async () =>
        {
            var _ = await repo.SearchAsync("\"unclosed quote and -", 0, 20);
        });

        Assert.Null(ex);
    }

    // --- Write-path drift ---

    [Fact]
    public async Task UpdatePage_RefreshesBodyPlainTextAndSearchVector()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        // Seed a page whose BodyPlainText contains "draft".
        var page = NewPage("Initial title", "p1", "draft content");
        ctx.WikiPages.Add(page);
        await ctx.SaveChangesAsync();

        // Simulate an update by mutating BodyPlainText and re-saving — same mechanism
        // WikiRepository.UpdatePageAsync uses (direct property mutation + SaveChangesAsync).
        // Postgres should recompute SearchVector from the STORED generated expression.
        page.BodyPlainText = "published final content";
        await ctx.SaveChangesAsync();

        // "published" now matches; "draft" no longer does.
        var (_, totalPublished) = await repo.SearchAsync("published", 0, 20);
        Assert.Equal(1, totalPublished);

        var (_, totalDraft) = await repo.SearchAsync("draft", 0, 20);
        Assert.Equal(0, totalDraft);
    }

    [Fact]
    public async Task RevertToVersion_RefreshesBodyPlainText()
    {
        await _fixture.ResetAsync();
        var repo = CreateRepo(out var ctx);
        await using var _ = ctx;

        // Seed a page with current-version body text "happening".
        var page = NewPage("Reverted page", "rp", "v2 body says happening");
        ctx.WikiPages.Add(page);
        await ctx.SaveChangesAsync();

        // Simulate a revert to v1 by restoring the older BodyPlainText.
        page.BodyPlainText = "v1 original draft";
        await ctx.SaveChangesAsync();

        // After revert, "original" matches (new body), "happening" does not (old body gone).
        var (_, totalOriginal) = await repo.SearchAsync("original", 0, 20);
        Assert.Equal(1, totalOriginal);

        var (_, totalHappening) = await repo.SearchAsync("happening", 0, 20);
        Assert.Equal(0, totalHappening);
    }
}
