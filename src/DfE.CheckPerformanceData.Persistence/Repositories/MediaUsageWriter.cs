using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// Brings the media_usages rows of some page versions into line with their content. It only adds,
// changes and removes tracked rows: the caller saves them in the same SaveChangesAsync as the page
// version, so a version and its usages are written together or not at all.
//
// A new row pins the image's current version. An image id that is not in the library is skipped,
// so content that names a deleted or unknown image still saves.
internal static class MediaUsageWriter
{
    public sealed record VersionContent(Guid VersionId, Guid PageNodeId, string Content);

    private readonly record struct UsageKey(Guid AssetId, string Location, string? WidgetId);

    /// <param name="keepPins">
    /// True keeps the pinned image version of a row that is still in the content (a rebuild
    /// repairs rows, it does not rewrite history). False moves it to the current version (a draft
    /// edited in place shows the current files).
    /// </param>
    /// <returns>The number of rows added, changed and removed.</returns>
    public static async Task<int> StageAsync(
        IPortalDbContext context, IReadOnlyCollection<VersionContent> versions, bool keepPins, CancellationToken ct = default)
    {
        if (versions.Count == 0) return 0;

        var references = versions.ToDictionary(
            v => v.VersionId,
            v => MediaReferenceExtractor.Extract(v.Content));

        var assetIds = references.Values.SelectMany(r => r).Select(r => r.AssetId).Distinct().ToList();
        var currentVersions = assetIds.Count == 0
            ? []
            : await context.MediaAssets
                .Where(a => assetIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.CurrentVersion, ct);

        var versionIds = references.Keys.ToList();
        var existing = (await context.MediaUsages
                .Where(u => versionIds.Contains(u.PageNodeVersionId))
                .ToListAsync(ct))
            .ToLookup(u => u.PageNodeVersionId);

        var changed = 0;
        foreach (var version in versions)
        {
            var wanted = references[version.VersionId]
                .Where(r => currentVersions.ContainsKey(r.AssetId))
                .Select(r => new UsageKey(r.AssetId, r.Location, r.WidgetPath))
                .ToHashSet();

            foreach (var row in existing[version.VersionId])
            {
                var key = new UsageKey(row.AssetId, row.Location, row.WidgetId);
                if (!wanted.Remove(key))
                {
                    // No longer in the content, or a second row for the same use.
                    context.MediaUsages.Remove(row);
                    changed++;
                }
                else if (!keepPins && row.AssetVersion != currentVersions[row.AssetId])
                {
                    row.AssetVersion = currentVersions[row.AssetId];
                    changed++;
                }
            }

            foreach (var key in wanted)
            {
                context.MediaUsages.Add(new MediaUsage
                {
                    Id = Guid.NewGuid(),
                    AssetId = key.AssetId,
                    AssetVersion = currentVersions[key.AssetId],
                    PageNodeId = version.PageNodeId,
                    PageNodeVersionId = version.VersionId,
                    Location = key.Location,
                    WidgetId = key.WidgetId,
                });
                changed++;
            }
        }

        return changed;
    }

    public static Task<int> StageAsync(IPortalDbContext context, PageNodeVersion version, bool keepPins) =>
        StageAsync(context, [new VersionContent(version.Id, version.PageNodeId, version.Content)], keepPins);
}
