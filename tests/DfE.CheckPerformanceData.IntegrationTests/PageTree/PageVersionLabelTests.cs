using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

// Version numbers in the page history, through the real editor actions on a real database. Every
// version of a page shows its own number, and a version keeps its number whatever happens to it.
[Collection(nameof(PostgresCollection))]
public sealed class PageVersionLabelTests(PostgresFixture postgres) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var ctx = postgres.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(@"TRUNCATE ""PageNodes"", ""PageNodeVersions"" RESTART IDENTITY CASCADE;");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task WithServiceAsync(Func<PageNodeService, Task> work)
    {
        await using var ctx = postgres.CreateContext();
        await work(new PageNodeService(new PageNodeRepository(ctx)));
    }

    private async Task<List<PageNodeVersionDto>> VersionsAsync(Guid page)
    {
        await using var ctx = postgres.CreateContext();
        return await new PageNodeService(new PageNodeRepository(ctx)).GetVersionsAsync(page);
    }

    private static List<string> Labels(IReadOnlyList<PageNodeVersionDto> versions) =>
        versions.OrderBy(v => v.VersionId).Select(v => PageVersionNumbering.Label(v)).ToList();

    [Fact]
    public async Task PublishingAndSavingAsUsual_NumbersTheDraftAsItWillPublish()
    {
        Guid page = default;
        await WithServiceAsync(async s => page = (await s.CreatePageAsync(null, "usual", "Usual", "content", "test")).Id);
        await WithServiceAsync(s => s.SaveWorkingContentAsync(page, "[]", "", "test"));
        await WithServiceAsync(s => s.PublishDraftAsync(page, "test"));
        await WithServiceAsync(s => s.SaveWorkingContentAsync(page, "[1]", "", "test"));

        Assert.Equal(["1", "2"], Labels(await VersionsAsync(page)));

        await WithServiceAsync(s => s.PublishDraftAsync(page, "test"));
        var versions = await VersionsAsync(page);
        Assert.Equal(["1", "2"], Labels(versions));
        Assert.Equal(2, versions.Single(v => v.IsCurrent).VersionId);
    }

    // Publish 2, schedule 3, unpublish 2, then edit and save it: version 2 is a draft again below
    // the scheduled version 3. The two keep their own numbers.
    [Fact]
    public async Task UnpublishingAndEditingAVersionBelowAScheduledOne_KeepsEveryNumberDifferent()
    {
        Guid page = default;
        await WithServiceAsync(async s => page = (await s.CreatePageAsync(null, "editor-route", "Editor route", "content", "test")).Id);
        await WithServiceAsync(s => s.SaveWorkingContentAsync(page, "[\"one\"]", "", "test"));
        await WithServiceAsync(s => s.PublishDraftAsync(page, "test"));
        await WithServiceAsync(s => s.SaveWorkingContentAsync(page, "[\"two\"]", "", "test"));
        await WithServiceAsync(s => s.PublishDraftAsync(page, "test"));
        await WithServiceAsync(s => s.SaveWorkingContentAsync(page, "[\"three\"]", "", "test"));
        var from = DateTime.UtcNow.AddDays(7);
        await WithServiceAsync(s => s.PublishAsync(page, 3, from, null, "test"));
        await WithServiceAsync(s => s.UnpublishAsync(page, "test"));
        await WithServiceAsync(s => s.SaveWorkingContentAsync(page, "[\"two, edited\"]", "", "test"));

        var versions = await VersionsAsync(page);
        Assert.Equal(["1", "2", "3"], Labels(versions));
        Assert.Contains("two, edited", versions.Single(v => v.VersionId == 2).Content);
    }
}
