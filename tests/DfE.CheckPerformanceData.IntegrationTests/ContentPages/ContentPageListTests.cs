using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentPages;

// The admin index lists every content page, so the repository needs a lightweight all-pages read
// that reports each page's published state without loading its content. Soft-deleted pages are
// excluded (the entity's query filter), and the list is ordered by title for a stable admin view.
[Collection(nameof(PostgresCollection))]
public sealed class ContentPageListTests(PostgresFixture fixture)
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

    private ContentPageService Service() => new(new ContentPageRepository(_fixture.CreateContext()));
    private ContentPageRepository Repo() => new(_fixture.CreateContext());

    [Fact]
    public async Task GetAll_ReturnsEveryPage_WithItsPublishedState()
    {
        await TruncateAsync();
        await Service().CreateAsync("alpha", "Alpha", null, "nav-content");
        await Service().CreateAsync("beta", "Beta", null, "full");
        await Service().SaveDraftAsync("beta", """[{"kind":"widget","type":"divider"}]""");
        await Service().PublishAsync("beta");

        var all = await Repo().GetAllAsync();

        Assert.Equal(2, all.Count);

        var alpha = all.Single(p => p.Slug == "alpha");
        Assert.Equal("Alpha", alpha.Title);
        Assert.Null(alpha.PublishedVersionNumber);
        Assert.False(alpha.IsPublished);

        var beta = all.Single(p => p.Slug == "beta");
        Assert.Equal(1, beta.PublishedVersionNumber);
        Assert.True(beta.IsPublished);
    }

    [Fact]
    public async Task GetAll_OrdersByTitle()
    {
        await TruncateAsync();
        await Service().CreateAsync("g", "Gamma", null, "nav-content");
        await Service().CreateAsync("a", "Alpha", null, "nav-content");
        await Service().CreateAsync("b", "Beta", null, "nav-content");

        var titles = (await Repo().GetAllAsync()).Select(p => p.Title);

        Assert.Equal(["Alpha", "Beta", "Gamma"], titles);
    }

    [Fact]
    public async Task GetAll_ReturnsEmpty_WhenNoPages()
    {
        await TruncateAsync();

        Assert.Empty(await Repo().GetAllAsync());
    }
}
