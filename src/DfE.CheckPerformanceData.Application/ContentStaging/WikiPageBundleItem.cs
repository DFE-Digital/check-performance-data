namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A single wiki page in an export bundle. Parentage is carried by slug path, not by the
// database id (which differs between environments): SlugPath is the full path of slugs from
// the root, ParentSlugPath is that path with the final segment removed (empty for a root page).
// Only the current content is exported — version history is not part of the v1 bundle.
public sealed record WikiPageBundleItem
{
    public string SlugPath { get; init; } = string.Empty;
    public string ParentSlugPath { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Content { get; init; }
}
