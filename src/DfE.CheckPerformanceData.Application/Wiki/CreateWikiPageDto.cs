namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class CreateWikiPageDto
{
    public string Title { get; init; } = string.Empty;
    public string? Content { get; init; }
    public int? ParentId { get; init; }

    // An explicit URL slug. When empty the slug is generated from the title; when set it is used
    // verbatim (slugified) so an editor can pin a slug for SEO independently of the title.
    public string? Slug { get; init; }

    // When set, the new page is placed at this position among its siblings instead of being
    // appended to the end. Used by content-staging import to reproduce the source ordering.
    public int? SortOrder { get; init; }

    // When set, the page is created with this stable cross-environment identity instead of a
    // freshly generated one. Used by content-staging import to preserve identity across envs.
    public Guid? ContentId { get; init; }
}
