using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Reports;
using DfE.CheckPerformanceData.Application.Media.RichText;
using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>
/// Groups the current uses of one image by how the page shows it, with the rules the renderers
/// use. So two uses that look the same on the page are in one group.
/// <list type="bullet">
/// <item>An Image widget is keyed on its crop, aspect ratio and focal point, rounded as a rendition
/// URL rounds them. A focal point with no aspect ratio is dropped. An SVG or an animation is never
/// cut, so its widget uses are the original. The widget's width and alignment are not part of the
/// key: they place the image, and do not change which part of it shows.</item>
/// <item>A rich-text img is keyed on the box that the page gives it. A box of the file's own size
/// is the original.</item>
/// <item>A version that records a use that the reader cannot see (a markdown image, say) shows
/// the image whole, so it counts as the original.</item>
/// </list>
/// The original comes first, then the cuts, then the rich-text boxes, each in the order that the
/// content first uses them. Uses are pages in title order, then blocks in key order.
/// </summary>
public static class MediaVariantGrouper
{
    private sealed record Owner(Guid? PageNodeId, int? ContentBlockId, string Name);

    private sealed record Seen(MediaVariantShape Shape, Owner Owner, string Location, EmbeddedVersionState? State, int Count);

    public static IReadOnlyList<MediaVariant> Group(MediaAssetVersionDto version,
        IEnumerable<MediaVariantPageVersion> pages, IEnumerable<MediaVariantBlock> blocks)
    {
        var assetId = version.AssetId;
        var seen = new List<Seen>();

        foreach (var page in pages)
        {
            var owner = new Owner(page.PageNodeId, null, page.Title);
            Add(seen, owner, page.State, MediaImageUseReader.ReadShapes(page.Content), page.RecordedAssetIds, version);
        }

        foreach (var block in blocks)
        {
            var owner = new Owner(null, block.Id, block.Key);
            Add(seen, owner, null, MediaImageUseReader.ReadRichTextShapes(block.Value), block.RecordedAssetIds, version);
        }

        return seen
            .Select((s, index) => (s, index))
            .GroupBy(x => x.s.Shape)
            .OrderBy(g => Rank(g.Key))
            .ThenBy(g => g.Min(x => x.index))
            .Select(g => new MediaVariant(g.Key, Uses(g.Select(x => x.s))))
            .ToList();
    }

    private static void Add(List<Seen> seen, Owner owner, EmbeddedVersionState? state,
        IReadOnlyList<MediaImageShapeUse> uses, IReadOnlyList<Guid> recorded, MediaAssetVersionDto version)
    {
        var mine = uses.Where(u => u.AssetId == version.AssetId).ToList();
        if (mine.Count == 0)
        {
            if (recorded.Contains(version.AssetId))
                seen.Add(new Seen(MediaVariantShape.Original, owner, MediaUsageLocations.RichText, state, 1));
            return;
        }

        // One entry for each shape and location in this version, with how many times it shows.
        foreach (var group in mine.GroupBy(u => (Shape: ShapeOf(u, version), u.Location)))
            seen.Add(new Seen(group.Key.Shape, owner, group.Key.Location, state, group.Count()));
    }

    private static IReadOnlyList<MediaVariantUse> Uses(IEnumerable<Seen> seen) =>
        seen
            .GroupBy(s => (s.Owner, s.Location))
            .OrderBy(g => g.Key.Owner.ContentBlockId is null ? 0 : 1)
            .ThenBy(g => g.Key.Owner.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Location, StringComparer.Ordinal)
            .Select(g => new MediaVariantUse(
                g.Key.Owner.PageNodeId,
                g.Key.Owner.ContentBlockId,
                g.Key.Owner.Name,
                g.Key.Location,
                g.Max(s => s.Count),
                g.Where(s => s.State is not null).Select(s => s.State!.Value).Distinct().Order().ToList()))
            .ToList();

    private static int Rank(MediaVariantShape shape) => shape.IsOriginal ? 0 : shape.IsRichTextBox ? 2 : 1;

    private static MediaVariantShape ShapeOf(MediaImageShapeUse use, MediaAssetVersionDto version) =>
        use.Location == MediaUsageLocations.Widget ? WidgetShape(use, version) : RichTextShape(use, version);

    private static MediaVariantShape WidgetShape(MediaImageShapeUse use, MediaAssetVersionDto version)
    {
        if (!MediaUrlBuilder.IsResizable(version)) return MediaVariantShape.Original;
        if (use.Crop is null && use.Aspect is null) return MediaVariantShape.Original;

        MediaRendition cut;
        try
        {
            cut = MediaRenditionQuery.Normalise(null, use.Crop, use.Aspect, use.Focal);
        }
        catch (ArgumentException)
        {
            // The page would refuse the settings too, and show the image whole.
            return MediaVariantShape.Original;
        }

        if (cut.Aspect is null && cut.Crop is { X: 0, Y: 0, Width: 1, Height: 1 }) return MediaVariantShape.Original;
        return new MediaVariantShape(cut, null, null);
    }

    private static MediaVariantShape RichTextShape(MediaImageShapeUse use, MediaAssetVersionDto version)
    {
        var (width, height) = RichTextImageRewriter.Box(use.BoxWidth, use.BoxHeight, use.BoxMarker, version);
        if (width is null && height is null) return MediaVariantShape.Original;
        if (width == version.Width && height == version.Height) return MediaVariantShape.Original;
        return new MediaVariantShape(null, width, height);
    }
}
