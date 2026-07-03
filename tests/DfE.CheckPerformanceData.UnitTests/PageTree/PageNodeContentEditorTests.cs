using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.UnitTests.PageTree;

// Drives PageNodeContentEditor against a hand-written IPageNodeService fake. Each test round-trips
// through the JSON so the mutations (add/move/delete/update/anchorize) are verified against the real
// serialised tree, not internal state.
public class PageNodeContentEditorTests
{
    private readonly FakePageNodeService _service = new();
    private readonly Guid _nodeId = Guid.NewGuid();

    private PageNodeContentEditor Sut() => new(_service, new StubHtmlRenderer());

    private IReadOnlyList<ContentNode> CurrentTree() =>
        ContentPageJson.Deserialize(_service.ReadContent(_nodeId)) ?? [];

    // ── AddWidget ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddWidget_ToEmptyNode_TreeContainsOneWidget()
    {
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);

        var widget = Assert.IsType<WidgetNode>(Assert.Single(CurrentTree()));
        Assert.Equal("heading", widget.Type);
    }

    [Fact]
    public async Task AddWidget_WithDefaultProps_HasRegistryDefaults()
    {
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);

        var widget = Assert.IsType<WidgetNode>(CurrentTree()[0]);
        Assert.Equal(2, widget.GetInt("level"));   // registry default for heading
    }

    [Fact]
    public async Task AddWidget_TwoWidgets_BothAppearInOrder()
    {
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 1)], "divider", null);

        var tree = CurrentTree().Cast<WidgetNode>().ToList();
        Assert.Equal(2, tree.Count);
        Assert.Equal("heading", tree[0].Type);
        Assert.Equal("divider", tree[1].Type);
    }

    // ── Move ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MoveUp_SwapsWithPreviousSibling()
    {
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 1)], "divider", null);

        await Sut().MoveUpAsync(_nodeId, [new TreeStep(0, 1)], null);

        var tree = CurrentTree().Cast<WidgetNode>().ToList();
        Assert.Equal("divider", tree[0].Type);
        Assert.Equal("heading", tree[1].Type);
    }

    [Fact]
    public async Task MoveDown_SwapsWithNextSibling()
    {
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 1)], "divider", null);

        await Sut().MoveDownAsync(_nodeId, [new TreeStep(0, 0)], null);

        var tree = CurrentTree().Cast<WidgetNode>().ToList();
        Assert.Equal("divider", tree[0].Type);
        Assert.Equal("heading", tree[1].Type);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesNodeAtPath()
    {
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);

        await Sut().DeleteAsync(_nodeId, [new TreeStep(0, 0)], null);

        Assert.Empty(CurrentTree());
    }

    [Fact]
    public async Task Delete_FirstOfTwo_LeavesSecond()
    {
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 1)], "divider", null);

        await Sut().DeleteAsync(_nodeId, [new TreeStep(0, 0)], null);

        var widget = Assert.IsType<WidgetNode>(Assert.Single(CurrentTree()));
        Assert.Equal("divider", widget.Type);
    }

    // ── Edit-after-publish regression ────────────────────────────────────────

    [Fact]
    public async Task AddWidget_AfterPublish_ContinuesFromPublishedContent()
    {
        // Simulate the published state: add a widget and capture the serialised tree.
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);
        var publishedContent = _service.ReadContent(_nodeId);

        // Re-seed to represent the "published-only, no draft" state the service returns
        // via GetWorkingOrLatestContentAsync when PublishAsync has consumed the draft.
        _service.Seed(_nodeId, publishedContent);

        // Edit again — must build on the published content, not a blank document.
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 1)], "divider", null);

        var tree = CurrentTree().Cast<WidgetNode>().ToList();
        Assert.Equal(2, tree.Count);
        Assert.Equal("heading", tree[0].Type);
        Assert.Equal("divider", tree[1].Type);
    }

    // ── UpdateWidget ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateWidget_ReplacesProps()
    {
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);

        var newProps = new JsonObject { ["level"] = 2, ["text"] = "Key dates" };
        await Sut().UpdateWidgetAsync(_nodeId, [new TreeStep(0, 0)], newProps, null);

        var widget = Assert.IsType<WidgetNode>(CurrentTree()[0]);
        Assert.Equal("Key dates", widget.GetString("text"));
    }

    // ── HeadingAnchorizer ────────────────────────────────────────────────────

    [Fact]
    public async Task AddHeading_WithDefaultProps_GetsAnchorAssigned()
    {
        // Default heading props have text=""; AnchorAllocator assigns the "section" fallback.
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);

        var widget = Assert.IsType<WidgetNode>(CurrentTree()[0]);
        Assert.False(string.IsNullOrEmpty(widget.Anchor));
    }

    [Fact]
    public async Task UpdateHeading_WithText_AnchorReflectsText()
    {
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);

        await Sut().UpdateWidgetAsync(
            _nodeId,
            [new TreeStep(0, 0)],
            new JsonObject { ["level"] = 2, ["text"] = "Important section" },
            null);

        var widget = Assert.IsType<WidgetNode>(CurrentTree()[0]);
        Assert.Equal("important-section", widget.Anchor);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fakes
    // ═══════════════════════════════════════════════════════════════════════

    // Hand-fake IPageNodeService: stores content per nodeId and exposes it via
    // GetWorkingOrLatestContentAsync. Simulates both "has working draft" and
    // "published-only, no draft" states: in both cases the fake returns whatever was last
    // stored, mirroring the real service's draft-or-latest fallback.
    private sealed class FakePageNodeService : IPageNodeService
    {
        private readonly Dictionary<Guid, string> _content = new();

        /// <summary>Seeds initial JSON for a node (optional — empty node starts as "[]").</summary>
        public void Seed(Guid nodeId, string json) => _content[nodeId] = json;

        /// <summary>Returns the current stored JSON for assertions.</summary>
        public string ReadContent(Guid nodeId) =>
            _content.TryGetValue(nodeId, out var c) ? c : "[]";

        public Task<string?> GetWorkingOrLatestContentAsync(Guid nodeId) =>
            Task.FromResult<string?>(ReadContent(nodeId));

        public Task SaveWorkingContentAsync(
            Guid nodeId, string content, string bodyPlainText, string? userId)
        {
            _content[nodeId] = content;
            return Task.CompletedTask;
        }

        // Not called by PageNodeContentEditor — throw to catch accidental usage.
        public Task<List<PageNodeVersionDto>> GetVersionsAsync(Guid nodeId) => throw new NotSupportedException();
        public Task<PageNodeDto> CreatePageAsync(
            Guid? parentId, string segment, string title, string pageType, string? userId) =>
            throw new NotSupportedException();

        public Task<PageNodeDto?> GetNodeByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<List<PageNodeTreeItemDto>> GetTreeAsync() => throw new NotSupportedException();
        public Task<PageNodeDto?> GetNodeByPathAsync(string path) => throw new NotSupportedException();
        public Task<LivePageResult?> GetLivePageAsync(string path, DateTime nowUtc) => throw new NotSupportedException();
        public Task PublishAsync(Guid nodeId, int versionId, DateTime? from, DateTime? to, string? userId) => throw new NotSupportedException();
        public Task PublishDraftAsync(Guid nodeId, string? userId) => throw new NotSupportedException();
        public Task UnpublishAsync(Guid nodeId, string? userId) => throw new NotSupportedException();
        public Task<bool> IsPublishedAsync(Guid nodeId) => throw new NotSupportedException();
        public Task DeleteAsync(Guid nodeId, string? userId) => throw new NotSupportedException();
        public Task MoveAsync(Guid nodeId, string direction) => throw new NotSupportedException();

        public Task<RenameNodeResult> RenameNodeAsync(
            Guid id, string newSegment, string newTitle, string? newSubtitle, string? newPageName, string? userId) =>
            throw new NotSupportedException();
    }

    // Minimal IHtmlRenderingService stub: passes text through unchanged. Sufficient for unit-testing
    // the tree mutation logic; actual HTML stripping is tested separately in HtmlRenderingService tests.
    private sealed class StubHtmlRenderer : IHtmlRenderingService
    {
        public string? RenderHtml(string? content) => content;
        public string StripTagsToPlainText(string? sanitisedHtml) => sanitisedHtml ?? string.Empty;
    }
}
