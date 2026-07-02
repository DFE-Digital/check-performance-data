using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Web.Models.Guidance;

namespace DfE.CheckPerformanceData.Web.Services;

// Reproduces the two hand-built guidance pages as flat lists of content-page widgets, reading the
// prose from the same CMS content blocks the public pages render. Headings become "heading" widgets
// (level + text); block bodies become "richtext" widgets (raw stored HTML — sanitising happens later
// in the widget partial, exactly as EditableContent does on the public page). KS4 order and heading
// levels come from GuidancePage.Ks4June2026; the landing order mirrors Views/Guidance/Index.cshtml.
public static class GuidanceContentTreeBuilder
{
    public static async Task<IReadOnlyList<ContentNode>> BuildKs4Async(IContentBlockService blocks)
    {
        var page = GuidancePage.Ks4June2026;
        var tree = new List<ContentNode>
        {
            RichText(await ValueAsync(blocks, page.IntroBlockKey)),
            RichText(await ValueAsync(blocks, page.PublishedBlockKey)),
        };

        foreach (var section in page.Sections)
        {
            tree.Add(Heading(section.Level, section.NavTitle));
            tree.Add(RichText(await ValueAsync(blocks, section.BlockKey)));
        }

        return tree;
    }

    public static async Task<IReadOnlyList<ContentNode>> BuildLandingAsync(IContentBlockService blocks)
    {
        const string ns = "guidance-landing-";
        var tree = new List<ContentNode>
        {
            RichText(await ValueAsync(blocks, ns + "intro")),

            Heading(2, await ValueAsync(blocks, ns + "signin-heading")),
            RichText(await ValueAsync(blocks, ns + "signin")),

            Heading(3, await ValueAsync(blocks, ns + "alerts-heading")),
            RichText(await ValueAsync(blocks, ns + "alerts")),
        };

        foreach (var key in new[] { "ks4", "ks2", "1619" })
        {
            tree.Add(Heading(2, await ValueAsync(blocks, ns + key + "-heading")));
            tree.Add(RichText(await ValueAsync(blocks, ns + key + "-intro")));
            tree.Add(RichText(await ValueAsync(blocks, ns + key + "-cards")));
        }

        return tree;
    }

    private static async Task<string> ValueAsync(IContentBlockService blocks, string key) =>
        (await blocks.GetByKeyAsync(key))?.Value ?? string.Empty;

    private static WidgetNode Heading(int level, string text) => new()
    {
        Type = "heading",
        Props = new JsonObject { ["level"] = level, ["text"] = text },
    };

    private static WidgetNode RichText(string html) => new()
    {
        Type = "richtext",
        Props = new JsonObject { ["html"] = html },
    };
}
