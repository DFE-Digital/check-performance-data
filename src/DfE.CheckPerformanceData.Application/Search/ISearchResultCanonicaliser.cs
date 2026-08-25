namespace DfE.CheckPerformanceData.Application.Search;

/// <summary>
/// Collapses a set of page contributions and block contributions into a single
/// rank-ordered list where every URL appears exactly once. A block that lives
/// on the same URL as a page (or another block) folds into that URL's row —
/// the rendered result set is always a list of URLs, never a mix of page hits
/// and standalone block hits.
/// </summary>
/// <remarks>
/// <para>
/// The implementation is a pure function of its inputs — it must not perform
/// I/O, log, capture ambient state, or observe the clock. The purity invariant
/// is enforced by composition: the method is synchronous, no
/// <see cref="System.Threading.CancellationToken"/> parameter is accepted, no
/// <see cref="System.Threading.Tasks.Task"/> return type is used, and the
/// concrete implementation must take zero constructor dependencies. Adding an
/// async signature or a DI dependency to satisfy some future need would
/// require a signature change caught in code review.
/// </para>
/// <para>
/// URL equality is ordinal (<see cref="System.StringComparer.Ordinal"/>).
/// URLs arrive already normalised by the upstream services
/// (site-search prefixes <c>PageNode.Path</c> with a leading slash;
/// content-block search projects <c>LastSeenPath</c>) so no case-folding is
/// required at this layer.
/// </para>
/// </remarks>
public interface ISearchResultCanonicaliser
{
    /// <summary>
    /// Groups <paramref name="pages"/> and <paramref name="blocks"/> by URL,
    /// picks the aggregate rank as the maximum contributor rank per URL, and
    /// orders the result by aggregate rank descending with contributor-count
    /// descending as the tie-break.
    /// </summary>
    IReadOnlyList<CanonicalSearchHit> Canonicalise(
        IEnumerable<PageContribution> pages,
        IEnumerable<BlockContribution> blocks);
}
