using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ContentStaging;

// Schema-versioned export of CMS content (wiki pages + content blocks) for moving content
// between environments. The WikiPages / ContentBlocks collections are the canonical payload;
// the Schema / SchemaVersion / ExportedAtUtc / ExportedBy header fields are metadata only and
// are not considered when comparing two bundles for round-trip integrity.
public sealed class ContentBundle
{
    public const string CurrentSchema = "cpd-content-v1";

    // Numeric counterpart to the $schema name, bumped in lockstep whenever the bundle shape
    // changes. Carried explicitly so a reader can filter/branch on the version without parsing
    // the schema string; import rejects versions it does not understand.
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = CurrentSchema;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DateTime? ExportedAtUtc { get; init; }
    public string? ExportedBy { get; init; }

    public List<WikiPageBundleItem> WikiPages { get; init; } = [];
    public List<ContentBlockBundleItem> ContentBlocks { get; init; } = [];
}
