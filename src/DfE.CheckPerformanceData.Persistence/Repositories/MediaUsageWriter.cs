using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// Brings the media_usages rows of some page versions, or of a content block, into line with their
// content. It only adds, changes and removes tracked rows: the caller saves them in the same
// SaveChangesAsync as the content, so the content and its usages are written together or not at all.
//
// A new row pins the image's current version. An image id that is not in the library is skipped,
// so content that names a deleted or unknown image still saves.
internal static class MediaUsageWriter
{
    // Pins, when given, choose the image version a new row records (staging import only). A pin
    // to a version the image does not have is ignored, and the row records the current version.
    public sealed record VersionContent(Guid VersionId, Guid PageNodeId, string Content, IReadOnlyDictionary<Guid, int>? Pins = null);

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
        var currentVersions = await CurrentVersionsAsync(context, references.Values.SelectMany(r => r), ct);

        var versionIds = references.Keys.Cast<Guid?>().ToList();
        var existing = (await context.MediaUsages
                .Where(u => versionIds.Contains(u.PageNodeVersionId))
                .ToListAsync(ct))
            .ToLookup(u => u.PageNodeVersionId!.Value);

        var changed = 0;
        foreach (var version in versions)
        {
            changed += Reconcile(context, existing[version.VersionId], references[version.VersionId], currentVersions, keepPins,
                key => new MediaUsage
                {
                    Id = Guid.NewGuid(),
                    AssetId = key.AssetId,
                    AssetVersion = Pinned(version.Pins, key.AssetId, currentVersions[key.AssetId]),
                    PageNodeId = version.PageNodeId,
                    PageNodeVersionId = version.VersionId,
                    Location = key.Location,
                    WidgetId = key.WidgetId,
                });
        }

        return changed;
    }

    public static Task<int> StageAsync(IPortalDbContext context, PageNodeVersion version, bool keepPins) =>
        StageAsync(context, [new VersionContent(version.Id, version.PageNodeId, version.Content)], keepPins);

    /// <summary>
    /// Stages the usages of a block's current value. A block shows its value as rich text only when
    /// its type is Content; any other type shows text and uses no image. A new block (no id yet)
    /// gets its rows through the navigation, so the one save writes both. Rows always pin the
    /// current image version, because a block always shows the newest file.
    /// </summary>
    public static async Task<int> StageBlockAsync(IPortalDbContext context, ContentBlock block, CancellationToken ct = default)
    {
        var references = block.BlockType == "Content"
            ? MediaReferenceExtractor.ExtractRichText(block.Value)
            : [];
        var currentVersions = await CurrentVersionsAsync(context, references, ct);
        var existing = block.Id == 0
            ? []
            : await context.MediaUsages.Where(u => u.ContentBlockId == block.Id).ToListAsync(ct);

        return Reconcile(context, existing, references, currentVersions, keepPins: false,
            key => new MediaUsage
            {
                Id = Guid.NewGuid(),
                AssetId = key.AssetId,
                AssetVersion = currentVersions[key.AssetId],
                ContentBlock = block,
                Location = key.Location,
                WidgetId = key.WidgetId,
            });
    }

    private static int Pinned(IReadOnlyDictionary<Guid, int>? pins, Guid assetId, int current) =>
        pins is not null && pins.TryGetValue(assetId, out var pin) && pin >= 1 && pin <= current ? pin : current;

    private static async Task<Dictionary<Guid, int>> CurrentVersionsAsync(
        IPortalDbContext context, IEnumerable<MediaReference> references, CancellationToken ct)
    {
        var assetIds = references.Select(r => r.AssetId).Distinct().ToList();
        return assetIds.Count == 0
            ? []
            : await context.MediaAssets
                .Where(a => assetIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.CurrentVersion, ct);
    }

    // Removes rows no longer in the content (and second rows for one use), moves pins when asked,
    // and adds a row for each new use.
    private static int Reconcile(
        IPortalDbContext context, IEnumerable<MediaUsage> existing, IEnumerable<MediaReference> references,
        Dictionary<Guid, int> currentVersions, bool keepPins, Func<UsageKey, MediaUsage> create)
    {
        var wanted = references
            .Where(r => currentVersions.ContainsKey(r.AssetId))
            .Select(r => new UsageKey(r.AssetId, r.Location, r.WidgetPath))
            .ToHashSet();

        var changed = 0;
        foreach (var row in existing)
        {
            if (!wanted.Remove(new UsageKey(row.AssetId, row.Location, row.WidgetId)))
            {
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
            context.MediaUsages.Add(create(key));
            changed++;
        }

        return changed;
    }
}
