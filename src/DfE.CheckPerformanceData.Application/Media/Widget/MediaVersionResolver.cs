namespace DfE.CheckPerformanceData.Application.Media.Widget;

/// <summary>
/// Request-scoped <see cref="IMediaVersionResolver"/>. Each call loads only the images it has not
/// seen for that page version: one query for the images and their current versions, and, for a
/// historic page version, one more for the versions recorded for it. Images that are not in the
/// library are remembered too, so they are not looked up again.
/// </summary>
public sealed class MediaVersionResolver(IMediaAssetRepository assets) : IMediaVersionResolver
{
    private readonly Dictionary<(Guid? PageVersionId, Guid AssetId), ResolvedMedia?> _cache = [];

    public async Task<IReadOnlyDictionary<Guid, ResolvedMedia>> ResolveAsync(
        IEnumerable<Guid> assetIds, Guid? pageVersionId, CancellationToken ct = default)
    {
        var ids = assetIds.Distinct().ToList();
        var missing = ids.Where(id => !_cache.ContainsKey((pageVersionId, id))).ToList();

        if (missing.Count > 0)
        {
            var found = (await assets.GetManyAsync(missing, ct)).ToDictionary(a => a.Id);

            // One row is kept for each place the page version uses an image, so one image can have
            // more than one recorded version; the newest wins.
            var pinned = pageVersionId is { } versionId && found.Count > 0
                ? (await assets.GetPinnedVersionsAsync(versionId, found.Keys.ToList(), ct))
                    .GroupBy(v => v.AssetId)
                    .ToDictionary(g => g.Key, g => g.MaxBy(v => v.Version)!)
                : [];

            foreach (var id in missing)
            {
                _cache[(pageVersionId, id)] = found.TryGetValue(id, out var asset)
                    ? new ResolvedMedia(asset, pinned.GetValueOrDefault(id) ?? asset.Latest)
                    : null;
            }
        }

        var result = new Dictionary<Guid, ResolvedMedia>();
        foreach (var id in ids)
        {
            if (_cache[(pageVersionId, id)] is { } resolved) result[id] = resolved;
        }
        return result;
    }
}
