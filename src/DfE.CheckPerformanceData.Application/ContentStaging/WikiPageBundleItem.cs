namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A single wiki page in an export bundle. Identity and parentage are carried by stable GUIDs,
// not by the database id (environment-specific) or the slug/title (which an editor may change):
// Id is the page's cross-environment identity, ParentId its parent's (null for a root). The
// SlugPath/ParentSlugPath/Slug fields are informational (human-readable hierarchy + the URL slug)
// and are not used to match pages. Only the current content is exported — no version history.
public sealed record WikiPageBundleItem
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public string SlugPath { get; init; } = string.Empty;
    public string ParentSlugPath { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Content { get; init; }

    // Position of this page among its siblings, preserved so the target environment reproduces
    // the same ordering rather than appending in import order. Lower values sort first.
    public int SortOrder { get; init; }
}
