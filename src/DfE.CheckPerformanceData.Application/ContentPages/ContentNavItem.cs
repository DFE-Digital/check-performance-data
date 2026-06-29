namespace DfE.CheckPerformanceData.Application.ContentPages;

// One entry in a content page's auto-generated left-hand nav. An H2 heading becomes a top-level
// item; its following H3 headings become Children (rendered indented). Href is the in-page anchor.
public sealed record ContentNavItem(string Text, string Href, IReadOnlyList<ContentNavItem> Children);
