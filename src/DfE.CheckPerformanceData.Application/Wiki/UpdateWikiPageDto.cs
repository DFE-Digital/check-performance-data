namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class UpdateWikiPageDto
{
    public string Title { get; init; } = string.Empty;
    public string? Content { get; init; }

    // An explicit URL slug. When set, it is used verbatim (slugified) and the title no longer
    // drives the slug — so a renamed page keeps its SEO slug. When empty, a slug that already
    // differs from its title (a custom slug) is preserved; otherwise it follows the title.
    public string? Slug { get; init; }

    // When set, the page is repositioned to this place among its siblings. Used by
    // content-staging import (Replace) to reproduce the source ordering on existing pages.
    public int? SortOrder { get; init; }
}
