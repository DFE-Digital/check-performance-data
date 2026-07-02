using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Web.Models.Guidance;
using DfE.CheckPerformanceData.Web.Services;

namespace DfE.CheckPerformanceData.Application.UnitTests.Guidance;

// The two hand-built guidance pages are reproduced as flat lists of heading + rich-text widgets.
// The tree builder is the pure, testable core: it reads raw block values by key and lays them out
// in the exact document order of the originals. KS4 order/levels come from GuidancePage.Ks4June2026;
// landing order comes from the fixed key list mirroring Views/Guidance/Index.cshtml.
public class GuidanceContentTreeBuilderTests
{
    private sealed class FakeContentBlockService : IContentBlockService
    {
        private readonly Dictionary<string, string> _values;
        public FakeContentBlockService(Dictionary<string, string> values) => _values = values;

        public Task<ContentBlockDto?> GetByKeyAsync(string key) =>
            Task.FromResult(_values.TryGetValue(key, out var v)
                ? new ContentBlockDto { Key = key, Value = v }
                : null);

        public Task<List<ContentBlockDto>> GetAllAsync() => throw new NotImplementedException();
        public Task RecordLastSeenAsync(string key, string path) => throw new NotImplementedException();
        public Task<ContentBlockDto> SaveAsync(SaveContentBlockDto dto) => throw new NotImplementedException();
        public Task<List<ContentBlockVersionDto>> GetVersionsAsync(string key) => throw new NotImplementedException();
        public Task<ContentBlockDto> RevertToVersionAsync(string key, int versionId) => throw new NotImplementedException();
    }

    private static WidgetNode Widget(ContentNode node) => Assert.IsType<WidgetNode>(node);

    [Fact]
    public async Task Ks4_StartsWithIntroThenPublishedRichText()
    {
        var blocks = new FakeContentBlockService(new()
        {
            ["guidance-ks4-2026-intro"] = "<p>intro</p>",
            ["guidance-ks4-2026-published"] = "<p>published</p>",
        });

        var tree = await GuidanceContentTreeBuilder.BuildKs4Async(blocks);

        var w0 = Widget(tree[0]);
        Assert.Equal("richtext", w0.Type);
        Assert.Equal("<p>intro</p>", w0.GetString("html"));

        var w1 = Widget(tree[1]);
        Assert.Equal("richtext", w1.Type);
        Assert.Equal("<p>published</p>", w1.GetString("html"));
    }

    [Fact]
    public async Task Ks4_FirstSectionIsHeadingThenBodyRichText()
    {
        var blocks = new FakeContentBlockService(new()
        {
            ["guidance-ks4-2026-changes-since-2025"] = "<p>changes body</p>",
        });

        var tree = await GuidanceContentTreeBuilder.BuildKs4Async(blocks);

        // The two lede widgets come first; the first section's heading/body follow at index 2/3.
        var heading = Widget(tree[2]);
        Assert.Equal("heading", heading.Type);
        Assert.Equal(2, heading.GetInt("level"));
        Assert.Equal("Changes to the CYPMD service since 2025", heading.GetString("text"));

        var body = Widget(tree[3]);
        Assert.Equal("richtext", body.Type);
        Assert.Equal("<p>changes body</p>", body.GetString("html"));
    }

    [Fact]
    public async Task Ks4_LevelThreeSectionProducesLevelThreeHeading()
    {
        var blocks = new FakeContentBlockService(new());

        var tree = await GuidanceContentTreeBuilder.BuildKs4Async(blocks);

        var childHasDied = tree
            .OfType<WidgetNode>()
            .First(w => w.Type == "heading" && w.GetString("text") == "Child has died");

        Assert.Equal(3, childHasDied.GetInt("level"));
    }

    [Fact]
    public async Task Ks4_WidgetCountIsTwoPlusTwoPerSection()
    {
        var blocks = new FakeContentBlockService(new());

        var tree = await GuidanceContentTreeBuilder.BuildKs4Async(blocks);

        Assert.Equal(2 + 2 * GuidancePage.Ks4June2026.Sections.Count, tree.Count);
    }

    [Fact]
    public async Task Landing_ProducesFourteenWidgetsInExpectedSequence()
    {
        var blocks = new FakeContentBlockService(new()
        {
            ["guidance-landing-signin-heading"] = "How to sign in",
            ["guidance-landing-signin"] = "<p>signin body</p>",
        });

        var tree = await GuidanceContentTreeBuilder.BuildLandingAsync(blocks);

        Assert.Equal(14, tree.Count);

        var kinds = tree.OfType<WidgetNode>().Select(w => w.Type).ToArray();
        Assert.Equal(
            new[]
            {
                "richtext",            // intro
                "heading", "richtext", // signin
                "heading", "richtext", // alerts
                "heading", "richtext", "richtext", // ks4
                "heading", "richtext", "richtext", // ks2
                "heading", "richtext", "richtext", // 1619
            },
            kinds);

        // Heading levels: signin H2, alerts H3, then three H2 section headings.
        Assert.Equal(2, Widget(tree[1]).GetInt("level"));
        Assert.Equal(3, Widget(tree[3]).GetInt("level"));
        Assert.Equal(2, Widget(tree[5]).GetInt("level"));

        // Heading text comes from the -heading (Title) block; rich-text html from the content block.
        Assert.Equal("How to sign in", Widget(tree[1]).GetString("text"));
        Assert.Equal("<p>signin body</p>", Widget(tree[2]).GetString("html"));
    }

    [Fact]
    public async Task MissingBlockKey_YieldsEmptyString_NoThrow()
    {
        var blocks = new FakeContentBlockService(new());

        var tree = await GuidanceContentTreeBuilder.BuildKs4Async(blocks);

        Assert.Equal(string.Empty, Widget(tree[0]).GetString("html"));
    }
}
