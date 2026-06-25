using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Schema-versioned export of CMS content (wiki pages + content blocks) for moving content
// between environments. The WikiPages / ContentBlocks collections are the canonical payload;
// the Schema / ExportedAtUtc / ExportedBy header fields are metadata only and are not
// considered when comparing two bundles for round-trip integrity.
public sealed class ContentBundle
{
    public const string CurrentSchema = "cpd-content-v1";

    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = CurrentSchema;

    public DateTime? ExportedAtUtc { get; init; }
    public string? ExportedBy { get; init; }

    public List<WikiPageBundleItem> WikiPages { get; init; } = [];
    public List<ContentBlockBundleItem> ContentBlocks { get; init; } = [];
}
