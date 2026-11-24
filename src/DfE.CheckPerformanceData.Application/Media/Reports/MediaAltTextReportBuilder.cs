using DfE.CheckPerformanceData.Application.Media.Embedded;

namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>
/// Builds the accessibility report. A library image is checked on its default alt text, its file
/// name and its caption. A use is checked on the text that readers get: the page's or block's own
/// text, or the library's default when it gives none. A decorative use needs no text and is left
/// out, and so is a use of an image that is not in the library. The same use in several current
/// versions of one page (live and draft, say) is one row that names each version.
/// </summary>
public static class MediaAltTextReportBuilder
{
    public static MediaAltTextReport Build(
        IReadOnlyCollection<MediaReportAsset> assets,
        IReadOnlyCollection<MediaReportPageVersion> pageVersions,
        IReadOnlyCollection<MediaReportBlock> blocks)
    {
        var library = assets.ToDictionary(a => a.Id);

        var assetRows = assets
            .Select(a => (Asset: a, Problem: WeakAltText.Check(a.DefaultAltText, a.FileName, a.Caption)))
            .Where(x => x.Problem != AltTextProblem.None)
            .OrderByDescending(x => x.Asset.UpdatedAtUtc)
            .ThenBy(x => x.Asset.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new MediaAltTextAssetRow(x.Asset.Id, x.Asset.FileName, x.Asset.DefaultAltText, x.Problem, x.Asset.FolderId))
            .ToList();

        var pageRows = pageVersions
            .SelectMany(v => Weak(MediaImageUseReader.Read(v.Content), library)
                .Select(w => (Version: v, Weak: w)))
            .GroupBy(x => (x.Version.PageNodeId, x.Weak.Use.WidgetPath, x.Weak.Use.AssetId, x.Weak.Use.Location, x.Weak.Alt, x.Weak.FromLibrary))
            .Select(g =>
            {
                var first = g.First();
                var states = g.Select(x => x.Version.State).Distinct().Order().ToList();
                return (Changed: g.Max(x => x.Version.UpdatedAtUtc),
                    Row: Row(first.Weak, first.Version.PageNodeId, null, first.Version.Title, states));
            });

        var blockRows = blocks
            .SelectMany(b => Weak(MediaImageUseReader.ReadRichText(b.Value), library)
                .Distinct()
                .Select(w => (Changed: b.UpdatedAtUtc, Row: Row(w, null, b.Key, b.Key, []))));

        var useRows = pageRows.Concat(blockRows)
            .OrderByDescending(x => x.Changed)
            .ThenBy(x => x.Row.OwnerName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Row)
            .ToList();

        return new MediaAltTextReport(assetRows, useRows);
    }

    private sealed record WeakUse(MediaImageUse Use, MediaReportAsset Asset, string? Alt, bool FromLibrary, AltTextProblem Problem);

    private static IEnumerable<WeakUse> Weak(IReadOnlyList<MediaImageUse> uses, IReadOnlyDictionary<Guid, MediaReportAsset> library)
    {
        foreach (var use in uses)
        {
            if (use.Decorative || !library.TryGetValue(use.AssetId, out var asset)) continue;
            var fromLibrary = use.Alt is null;
            var alt = fromLibrary ? asset.DefaultAltText : use.Alt;
            var problem = WeakAltText.Check(alt, asset.FileName, use.Caption);
            if (problem != AltTextProblem.None) yield return new WeakUse(use, asset, alt, fromLibrary, problem);
        }
    }

    private static MediaAltTextUseRow Row(WeakUse weak, Guid? pageNodeId, string? blockKey, string ownerName, IReadOnlyList<EmbeddedVersionState> states) =>
        new(weak.Asset.Id, weak.Asset.FileName, pageNodeId, blockKey, ownerName, states,
            weak.Use.Location, weak.Alt, weak.FromLibrary, weak.Problem);
}
