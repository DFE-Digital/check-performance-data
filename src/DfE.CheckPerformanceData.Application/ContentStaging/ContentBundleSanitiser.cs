using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Runs every HTML-carrying field in a bundle through IHtmlRenderingService.RenderHtml so the
// DB never stores an untrusted script tag or javascript: URL, even if the render path later
// forgets to sanitise. Belt-and-braces relative to the existing render-time sanitisation:
// today Wiki.cshtml + the content-block "Content" view both sanitise on read, but a future
// raw reader (RSS, JSON API, plaintext export) would otherwise leak the unsanitised payload.
//
// Sanitisation is idempotent — running it twice on already-clean HTML is a no-op — so callers
// can invoke it at both Preview and Import time without double-scrubbing. Records with init-only
// members are updated by list-slot replacement (via `with { ... }`) so the bundle's outer shape
// stays intact.
public sealed class ContentBundleSanitiser(IHtmlRenderingService html)
{
    public int SanitiseInPlace(ContentBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var changed = 0;

        foreach (var page in bundle.PageNodes)
        {
            // Only wiki-typed pages carry raw HTML in their version body — content-typed
            // pages carry a widget-tree JSON whose rich-text nodes are already sanitised on
            // render, and the JSON structure would break if we passed it through an HTML
            // sanitiser directly.
            if (page.PageType != "wiki") continue;

            for (var i = 0; i < page.Versions.Count; i++)
            {
                var version = page.Versions[i];
                var clean = html.RenderHtml(version.Content) ?? string.Empty;
                if (!string.Equals(clean, version.Content, StringComparison.Ordinal))
                {
                    page.Versions[i] = version with { Content = clean };
                    changed++;
                }
            }
        }

        for (var i = 0; i < bundle.ContentBlocks.Count; i++)
        {
            var block = bundle.ContentBlocks[i];
            // Only "Content" blocks are rendered as HTML; other block types (e.g. "Title")
            // are rendered as plain text and pass through unchanged.
            if (block.BlockType != "Content") continue;

            var clean = html.RenderHtml(block.Value) ?? string.Empty;
            if (!string.Equals(clean, block.Value, StringComparison.Ordinal))
            {
                bundle.ContentBlocks[i] = block with { Value = clean };
                changed++;
            }
        }

        return changed;
    }
}
