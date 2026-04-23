namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class WikiPageSearchResultDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;

    // SlugPath is deliberately { get; set; } — the repo produces the row without it
    // (EF cannot express the parent walk in one query), and WikiService enriches it
    // after the results are in memory (pattern mirrors WikiService.BuildSlugPathAsync).
    public string SlugPath { get; set; } = string.Empty;

    public int? ParentId { get; init; }

    // HTML emitted by Postgres ts_headline. Contains only <mark>/</mark> wrappers plus
    // plain text from BodyPlainText (which is tag-stripped at write time). Rendered
    // with @Html.Raw in Search.cshtml — see 05-UI-SPEC.md §Security Contract.
    public string SnippetHtml { get; init; } = string.Empty;
}
