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

    // ── SaveDraft (#475) ─────────────────────────────────────────────────────
    //
    // The editor's Save button. A save with no edit must write the same content and the same
    // plain-text body as a widget edit. The old action wrote an empty body, and the published
    // page then lost its body from site search.

    private static readonly string[] TypesWithText = ["heading", "richtext", "card", "published"];
    private static readonly string[] TypesWithNoText = ["divider", "summarylist", "search", "results", "pagenav"];

    private PageNodeContentEditor SutWithRealRenderer() => new(_service, new HtmlRenderingService());

    private static WidgetNode Widget(string type, string propsJson) =>
        new() { Type = type, Props = JsonNode.Parse(propsJson)!.AsObject() };

    private void SeedTree(Guid nodeId, params ContentNode[] nodes) =>
        _service.Seed(nodeId, ContentPageJson.Serialize(nodes));

    [Fact]
    public async Task SaveDraft_DerivesTheBodyFromTheWidgetTree()
    {
        SeedTree(_nodeId,
            Widget("heading", """{"level":2,"text":"Key dates"}"""),
            Widget("richtext", """{"html":"<p>Check your data by 5pm.</p>"}"""));

        await SutWithRealRenderer().SaveDraftAsync(_nodeId, null);

        Assert.Equal("Key dates Check your data by 5pm.", _service.ReadBody(_nodeId));
    }

    [Fact]
    public async Task SaveDraft_GivesTheBodyAndTheContentThatAWidgetEditGives()
    {
        var saved = Guid.NewGuid();
        var edited = Guid.NewGuid();
        foreach (var node in new[] { saved, edited })
        {
            SeedTree(node,
                Widget("heading", """{"level":2,"text":"Key dates"}"""),
                Widget("card", """{"title":"Evidence","body":"Send one PDF file.","href":"/guidance"}"""));
        }

        await Sut().SaveDraftAsync(saved, null);
        // A widget edit that changes nothing: the heading is saved again with the same props.
        await Sut().UpdateWidgetAsync(edited, [new TreeStep(0, 0)],
            new JsonObject { ["level"] = 2, ["text"] = "Key dates" }, null);

        Assert.Equal("Key dates Evidence Send one PDF file.", _service.ReadBody(saved));
        Assert.Equal(_service.ReadBody(edited), _service.ReadBody(saved));
        Assert.Equal(_service.ReadContent(edited), _service.ReadContent(saved));
    }

    [Theory]
    [InlineData("heading", """{"level":2,"text":"Alpha heading"}""", "Alpha heading")]
    [InlineData("richtext", """{"html":"<p>Bravo <strong>paragraph</strong></p>"}""", "Bravo paragraph")]
    [InlineData("card", """{"title":"Charlie title","body":"Charlie body","href":"/x"}""", "Charlie title Charlie body")]
    [InlineData("published", """{"text":"Delta callout"}""", "Delta callout")]
    public async Task SaveDraft_IndexesTheTextOfEachWidgetTypeThatHasText(
        string type, string propsJson, string expectedBody)
    {
        Assert.Contains(type, TypesWithText);
        SeedTree(_nodeId, Widget(type, propsJson));

        await SutWithRealRenderer().SaveDraftAsync(_nodeId, null);

        Assert.Equal(expectedBody, _service.ReadBody(_nodeId));
    }

    [Theory]
    [InlineData("divider")]
    [InlineData("summarylist")]
    [InlineData("search")]
    [InlineData("results")]
    [InlineData("pagenav")]
    public async Task SaveDraft_IndexesNoTextForAWidgetTypeWithNoProse(string type)
    {
        Assert.Contains(type, TypesWithNoText);
        SeedTree(_nodeId, new WidgetNode { Type = type, Props = WidgetRegistry.CreateDefaultProps(type) });

        await Sut().SaveDraftAsync(_nodeId, null);

        Assert.Equal(string.Empty, _service.ReadBody(_nodeId));
    }

    [Fact]
    public void EveryRegisteredWidgetType_IsListedAsWithTextOrWithNoText()
    {
        // A new widget type must be added to one list above, with a case in its theory. This
        // makes "does its text go into site search?" a decision and not an accident.
        var registered = WidgetRegistry.All.Select(w => w.Type).Order();

        Assert.Equal(registered, TypesWithText.Concat(TypesWithNoText).Order());
    }

    [Fact]
    public async Task SaveDraft_IndexesWidgetsInsideARegion()
    {
        SeedTree(_nodeId, new RegionNode
        {
            Layout = RegionLayout.Halves,
            Columns =
            [
                [Widget("heading", """{"level":3,"text":"Left column"}""")],
                [Widget("published", """{"text":"Right column"}""")]
            ]
        });

        await Sut().SaveDraftAsync(_nodeId, null);

        Assert.Equal("Left column Right column", _service.ReadBody(_nodeId));
    }

    [Fact]
    public async Task SaveDraft_OnAPageWithNoContent_SavesAnEmptyTreeAndAnEmptyBody()
    {
        await Sut().SaveDraftAsync(_nodeId, null);

        Assert.Empty(CurrentTree());
        Assert.Equal(string.Empty, _service.ReadBody(_nodeId));   // not null: a save happened
    }

    [Fact]
    public async Task SaveDraft_OnADraftTheEditorWrote_ChangesNothing()
    {
        await Sut().AddWidgetAsync(_nodeId, [new TreeStep(0, 0)], "heading", null);
        await Sut().UpdateWidgetAsync(_nodeId, [new TreeStep(0, 0)],
            new JsonObject { ["level"] = 2, ["text"] = "Important section" }, null);
        var content = _service.ReadContent(_nodeId);

        await Sut().SaveDraftAsync(_nodeId, null);

        Assert.Equal(content, _service.ReadContent(_nodeId));
        Assert.Equal("Important section", _service.ReadBody(_nodeId));
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
        private readonly Dictionary<Guid, string> _body = new();

        /// <summary>The plain-text body of the last save, or null when nothing was saved.</summary>
        public string? ReadBody(Guid nodeId) => _body.TryGetValue(nodeId, out var b) ? b : null;

        /// <summary>Seeds initial JSON for a node (optional — empty node starts as "[]").</summary>
        public void Seed(Guid nodeId, string json) => _content[nodeId] = json;

        /// <summary>Returns the current stored JSON for assertions.</summary>
        public string ReadContent(Guid nodeId) =>
            _content.TryGetValue(nodeId, out var c) ? c : "[]";

        // Null when nothing is stored, as the real service returns for a node with no version.
        public Task<string?> GetWorkingOrLatestContentAsync(Guid nodeId) =>
            Task.FromResult(_content.TryGetValue(nodeId, out var c) ? c : null);

        public Task SaveWorkingContentAsync(
            Guid nodeId, string content, string bodyPlainText, string? userId)
        {
            _content[nodeId] = content;
            _body[nodeId] = bodyPlainText;
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
        public Task<List<PageNodeDto>> GetDeletedAsync() => throw new NotSupportedException();
        public Task RestoreAsync(Guid nodeId, string? userId) => throw new NotSupportedException();
        public Task<MoveNodeResult> MoveNodeAsync(Guid nodeId, Guid? newParentId, int newSortOrder, string? userId) => throw new NotSupportedException();
        public Task SetShowInMenuAsync(Guid nodeId, bool showInMenu, string? userId) => throw new NotSupportedException();
        public Task SetAppearInSearchAsync(Guid nodeId, bool appearInSearch, string? userId) => throw new NotSupportedException();
        public Task SetKeywordsAsync(Guid nodeId, string? keywords, string? userId) => throw new NotSupportedException();
        public Task MoveAsync(Guid nodeId, string direction) => throw new NotSupportedException();
        public Task<PageNodeDto?> CopyPageAsync(Guid sourceId, string? userId) => throw new NotSupportedException();

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
