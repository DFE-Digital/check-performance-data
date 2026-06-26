namespace DfE.CheckPerformanceData.Application.ContentStaging;

// A single wiki page in an export bundle. Identity and parentage are carried purely by stable
// GUIDs: Id is the page's cross-environment identity, ParentId its parent's (null for a root).
// The slug/title are not used to match — import recognises a page only by Id. The URL slug is
// regenerated from the Title on import, and a display slug path is built virtually by walking the
// ParentId chain. Only the current content is exported — no version history.
public sealed record WikiPageBundleItem
{
    public Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public string Title { get; init; } = string.Empty;

    // The URL slug, carried so a custom (SEO-pinned) slug survives export/import. It is NOT used to
    // match pages — that is still purely by Id — only to set the slug on the imported page.
    public string Slug { get; init; } = string.Empty;
    public string? Content { get; init; }

    // Position of this page among its siblings, preserved so the target environment reproduces
    // the same ordering rather than appending in import order. Lower values sort first.
    public int SortOrder { get; init; }
}
