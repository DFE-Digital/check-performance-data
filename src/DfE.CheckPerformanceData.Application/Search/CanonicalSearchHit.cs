namespace DfE.CheckPerformanceData.Application.Search;

/// <summary>
/// A single page-corpus match contributing to a canonical URL row. Title and
/// snippet are pre-computed by the upstream site-search service; the
/// canonicaliser only decides which contributor's snippet to surface.
/// </summary>
/// <param name="Url">Canonical URL of the page (leading slash included).</param>
/// <param name="Title">Display title used when the page is the winning contributor.</param>
/// <param name="SnippetHtml">Safe snippet HTML (tag-stripped, matched term wrapped in <c>&lt;mark&gt;</c>).</param>
/// <param name="Rank">Raw <c>ts_rank</c> of this page contribution — the comparison key for MAX aggregation and snippet selection.</param>
/// <param name="PageId">Source <c>PageNode.Id</c>. Not surfaced by the canonicaliser; passed through for downstream telemetry.</param>
public sealed record PageContribution(
    string Url,
    string Title,
    string SnippetHtml,
    float Rank,
    Guid PageId);

/// <summary>
/// A single block-corpus match contributing to a canonical URL row. When no
/// page contribution exists for the same URL, <see cref="PageTitle"/> is used
/// as the row title. The block's key is preserved so the rendered row can list
/// the contributing block keys under a debug-only editor affordance.
/// </summary>
/// <param name="Url">Canonical URL of the host page (leading slash included).</param>
/// <param name="PageTitle">Title of the host page — used when the row has block contributors but no page contributor.</param>
/// <param name="SnippetHtml">Safe snippet HTML (tag-stripped, matched term wrapped in <c>&lt;mark&gt;</c>).</param>
/// <param name="Rank">Raw <c>ts_rank</c> of this block contribution — the comparison key for MAX aggregation and snippet selection.</param>
/// <param name="BlockKey">Content-block key (e.g. <c>hero</c>, <c>footer-cta</c>).</param>
public sealed record BlockContribution(
    string Url,
    string PageTitle,
    string SnippetHtml,
    float Rank,
    string BlockKey);

/// <summary>
/// One canonical row on the merged search results list. Every URL appears
/// exactly once, with all its page and block contributors folded in. The
/// aggregate rank is the maximum contributor rank; the snippet is drawn from
/// the single highest-ranked contributor.
/// </summary>
/// <param name="Url">Canonical URL for the row.</param>
/// <param name="Title">Display title (from the page contributor if present, otherwise from the first block contributor's <see cref="BlockContribution.PageTitle"/>).</param>
/// <param name="SnippetHtml">Snippet HTML drawn from the single highest-ranked contributor for this URL.</param>
/// <param name="AggregateRank">Maximum of all contributor ranks — the primary sort key.</param>
/// <param name="PageContributorCount">Number of page contributors folded into this row (0 or 1).</param>
/// <param name="BlockContributorCount">Number of block contributors folded into this row (0..N).</param>
/// <param name="ContributingBlockKeys">Block keys, in input order, of every block contribution folded into this row.</param>
public sealed record CanonicalSearchHit(
    string Url,
    string Title,
    string SnippetHtml,
    float AggregateRank,
    int PageContributorCount,
    int BlockContributorCount,
    IReadOnlyList<string> ContributingBlockKeys);
