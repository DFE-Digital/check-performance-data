using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Schema-versioned export of CMS content (page-tree nodes, content blocks and home banners) for
// moving content between environments. The PageNodes / ContentBlocks / HomeBanners collections
// are the canonical payload; the Schema / SchemaVersion / ExportedAtUtc / ExportedBy header
// fields are metadata only and are not considered when comparing two bundles for round-trip
// integrity.
//
// v2 is the current shape: every page (folder, content, wiki-typed) is a PageNode, so the
// whole page tree round-trips through the same shape. Legacy v1 bundles are not accepted.
public sealed class ContentBundle
{
    public const string CurrentSchema = "cpd-content-v2";

    // Numeric counterpart to the $schema name, bumped in lockstep whenever the bundle shape
    // changes. Carried explicitly so a reader can filter/branch on the version without parsing
    // the schema string; import rejects versions it does not understand.
    public const int CurrentSchemaVersion = 2;

    private readonly string _schema = CurrentSchema;

    [JsonPropertyName("$schema")]
    public string Schema { get => _schema; init => _schema = BundleMemberDefaults.OrEmpty(value); }

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DateTime? ExportedAtUtc { get; init; }
    public string? ExportedBy { get; init; }

    private readonly List<PageNodeBundleItem> _pageNodes = [];
    public List<PageNodeBundleItem> PageNodes
    {
        get => _pageNodes;
        init => _pageNodes = BundleMemberDefaults.NonNullItems(value);
    }

    private readonly List<ContentBlockBundleItem> _contentBlocks = [];
    public List<ContentBlockBundleItem> ContentBlocks
    {
        get => _contentBlocks;
        init => _contentBlocks = BundleMemberDefaults.NonNullItems(value);
    }

    // Start-page banners (#566). Optional and additive: a bundle written before banners existed
    // deserialises with an empty list, so the schema name and version are unchanged.
    private readonly List<HomeBannerBundleItem> _homeBanners = [];
    public List<HomeBannerBundleItem> HomeBanners
    {
        get => _homeBanners;
        init => _homeBanners = BundleMemberDefaults.NonNullItems(value);
    }
}
