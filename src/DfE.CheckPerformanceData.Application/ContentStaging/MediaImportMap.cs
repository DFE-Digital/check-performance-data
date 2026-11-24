namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Where each image version in a bundle landed in the target library. Import cannot keep the
// exporting environment's version numbers (a target image may already have versions of its
// own), so a page version's pins are translated through this before its usages are saved.
public sealed class MediaImportMap
{
    private readonly Dictionary<(Guid MediaId, int Version), int> _versions = [];

    public static MediaImportMap Empty => new();

    public void Add(Guid mediaId, int bundleVersion, int targetVersion) =>
        _versions[(mediaId, bundleVersion)] = targetVersion;

    public int? TargetVersion(Guid mediaId, int bundleVersion) =>
        _versions.TryGetValue((mediaId, bundleVersion), out var target) ? target : null;

    /// <summary>
    /// The target version each pinned image should show, by image id. A pin to a version that
    /// did not land is left out, so that use shows the image's current version.
    /// </summary>
    public IReadOnlyDictionary<Guid, int> MapPins(IEnumerable<MediaPinBundleItem> pins)
    {
        var mapped = new Dictionary<Guid, int>();
        foreach (var pin in pins)
        {
            if (TargetVersion(pin.MediaId, pin.Version) is int target) mapped[pin.MediaId] = target;
        }
        return mapped;
    }
}
