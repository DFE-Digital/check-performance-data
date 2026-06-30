using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// The editor service turns an edit action (add a widget/region, move, delete, change props) into a
// load-draft → mutate-tree → re-anchor-headings → save-draft round trip. These tests drive it against
// an in-memory draft and assert the resulting tree, so the mutation + anchoring logic is pinned
// without a database or a view.
public class ContentPageEditorTests
{
    private readonly FakeContentPageRepository _repo = new();

    private ContentPageEditor Sut() => new(_repo);

    private IReadOnlyList<ContentNode> Draft() =>
        ContentPageJson.Deserialize(_repo.Draft) ?? [];

    [Fact]
    public async Task AddWidget_AppendsToRoot_WithDefaultPropsAndAnchor()
    {
        await Sut().AddWidgetAsync("ks4", [new TreeStep(0, 0)], "heading");

        var widget = Assert.IsType<WidgetNode>(Assert.Single(Draft()));
        Assert.Equal("heading", widget.Type);
        Assert.Equal(2, widget.GetInt("level"));      // default props from the registry
        Assert.False(string.IsNullOrEmpty(widget.Anchor));
    }

    [Fact]
    public async Task AddWidget_TwoHeadings_GetUniqueAnchors()
    {
        await Sut().AddWidgetAsync("ks4", [new TreeStep(0, 0)], "heading");
        await Sut().AddWidgetAsync("ks4", [new TreeStep(0, 1)], "heading");

        var anchors = Draft().Cast<WidgetNode>().Select(w => w.Anchor).ToList();
        Assert.Equal(anchors.Count, anchors.Distinct().Count());
    }

    [Fact]
    public async Task AddRegion_InsertsRegion_WithEmptyColumnsPerLayout()
    {
        await Sut().AddRegionAsync("ks4", [new TreeStep(0, 0)], RegionLayout.Thirds);

        var region = Assert.IsType<RegionNode>(Assert.Single(Draft()));
        Assert.Equal(RegionLayout.Thirds, region.Layout);
        Assert.Equal(3, region.Columns.Count);
        Assert.All(region.Columns, c => Assert.Empty(c));
    }

    [Fact]
    public async Task AddWidget_IntoARegionColumn_NestsIt()
    {
        await Sut().AddRegionAsync("ks4", [new TreeStep(0, 0)], RegionLayout.Halves);

        await Sut().AddWidgetAsync("ks4", [new TreeStep(0, 0), new TreeStep(1, 0)], "divider");

        var region = Assert.IsType<RegionNode>(Draft()[0]);
        Assert.Equal("divider", Assert.IsType<WidgetNode>(region.Columns[1][0]).Type);
    }

    [Fact]
    public async Task MoveDown_ReordersSiblings()
    {
        await Sut().AddWidgetAsync("ks4", [new TreeStep(0, 0)], "divider");
        await Sut().AddWidgetAsync("ks4", [new TreeStep(0, 1)], "heading");

        await Sut().MoveDownAsync("ks4", [new TreeStep(0, 0)]);

        Assert.Equal("heading", Assert.IsType<WidgetNode>(Draft()[0]).Type);
        Assert.Equal("divider", Assert.IsType<WidgetNode>(Draft()[1]).Type);
    }

    [Fact]
    public async Task Delete_RemovesTheNode()
    {
        await Sut().AddWidgetAsync("ks4", [new TreeStep(0, 0)], "divider");

        await Sut().DeleteAsync("ks4", [new TreeStep(0, 0)]);

        Assert.Empty(Draft());
    }

    [Fact]
    public async Task UpdateWidget_ReplacesProps_AndReanchorsHeadingFromNewText()
    {
        await Sut().AddWidgetAsync("ks4", [new TreeStep(0, 0)], "heading");

        await Sut().UpdateWidgetAsync("ks4", [new TreeStep(0, 0)],
            new JsonObject { ["level"] = 2, ["text"] = "Key dates" });

        var widget = Assert.IsType<WidgetNode>(Draft()[0]);
        Assert.Equal("Key dates", widget.GetString("text"));
        Assert.Equal("key-dates", widget.Anchor);
    }

    // Minimal in-memory repository: the editor only needs to read the current draft and write it back.
    private sealed class FakeContentPageRepository : IContentPageRepository
    {
        public string Draft { get; private set; } = "[]";

        public Task<ContentPageDto?> GetBySlugAsync(string slug) =>
            Task.FromResult<ContentPageDto?>(new ContentPageDto
            {
                Id = 1,
                Slug = slug,
                Title = "KS4",
                Layout = "nav-content",
                DraftContent = Draft
            });

        public Task UpdateDraftAsync(int id, string draftContent)
        {
            Draft = draftContent;
            return Task.CompletedTask;
        }

        public Task<ContentPageDto> CreateAsync(string slug, string title, string? caption, string layout, string draftContent, Guid? contentId = null) => throw new NotSupportedException();
        public Task<List<ContentPageSummaryDto>> GetAllAsync() => throw new NotSupportedException();
        public Task<int> GetMaxVersionNumberAsync(int contentPageId) => throw new NotSupportedException();
        public Task AddVersionAsync(int contentPageId, int versionNumber, string snapshot, string title) => throw new NotSupportedException();
        public Task SetPublishedVersionAsync(int id, int versionNumber) => throw new NotSupportedException();
        public Task<ContentPageVersionDto?> GetVersionAsync(int contentPageId, int versionNumber) => throw new NotSupportedException();
        public Task<List<ContentPageVersionDto>> GetVersionsAsync(int contentPageId) => throw new NotSupportedException();
        public Task ExecuteInTransactionAsync(Func<Task> work) => throw new NotSupportedException();
    }
}
