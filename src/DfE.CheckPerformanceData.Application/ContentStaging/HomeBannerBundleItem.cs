namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A start-page banner in an export bundle (#566). Matched across environments by the stable Id
// (HomeBanner.ContentId). Dates are UK wall-clock values with no offset. Versions travel with it,
// oldest first; an import of a NEW banner replays them, an import over an EXISTING banner adds one
// new version from the current fields (the same as content blocks).
public sealed record HomeBannerBundleItem
{
    public Guid Id { get; init; }
    private readonly string _heading = string.Empty;
    public string Heading { get => _heading; init => _heading = BundleMemberDefaults.OrEmpty(value); }
    private readonly string _body = string.Empty;
    public string Body { get => _body; init => _body = BundleMemberDefaults.OrEmpty(value); }
    public bool IsEnabled { get; init; }
    public DateTime? ShowFrom { get; init; }
    public DateTime? ShowUntil { get; init; }
    public int SortOrder { get; init; }

    private readonly List<HomeBannerVersionBundleItem> _versions = [];
    public List<HomeBannerVersionBundleItem> Versions
    {
        get => _versions;
        init => _versions = BundleMemberDefaults.NonNullItems(value);
    }
}

public sealed record HomeBannerVersionBundleItem
{
    public int VersionNumber { get; init; }
    private readonly string _heading = string.Empty;
    public string Heading { get => _heading; init => _heading = BundleMemberDefaults.OrEmpty(value); }
    private readonly string _body = string.Empty;
    public string Body { get => _body; init => _body = BundleMemberDefaults.OrEmpty(value); }
    public bool IsEnabled { get; init; }
    public DateTime? ShowFrom { get; init; }
    public DateTime? ShowUntil { get; init; }
    public DateTime CreatedAt { get; init; }
}
