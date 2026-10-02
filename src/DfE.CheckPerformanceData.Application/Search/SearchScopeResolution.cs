using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.Search;

// What a search is limited to once the pages a request names have been looked up.
//   Paths        - the canonical comma-separated page paths to search (each with everything
//                  beneath it), or null for the whole site.
//   NamesNothing - the request named pages but none of them exists any more. The search then
//                  finds nothing, the same as a path scope naming a page that has gone, rather
//                  than quietly widening to the whole site.
public sealed record ResolvedSearchScope(string? Paths, bool NamesNothing)
{
    public static ResolvedSearchScope WholeSite { get; } = new(null, false);
}

// Turns the two ways a request can name pages into one path scope:
//   scope=a/b,c/d  page paths, the original form, still accepted for existing links and widgets;
//   pages=t1,t2    page tokens (see PageToken), which keep links short and survive renames.
// When a request carries both, the search covers the pages named by either.
public static class SearchScopeResolution
{
    // Tokens are matched against every current (non-deleted) page, worked out per request: there
    // are a few hundred pages at most, and nothing stored means nothing to go stale when a page
    // is renamed, moved or deleted. A request without tokens never touches the page list.
    public static async Task<ResolvedSearchScope> ResolveSearchScopeAsync(
        this IPageNodeRepository pages, string? scopePaths, string? pageTokens)
    {
        var paths = SearchScope.Parse(scopePaths);
        var tokens = PageToken.ParseList(pageTokens);
        if (tokens.Count == 0) return new ResolvedSearchScope(SearchScope.Normalise(scopePaths), false);

        var live = await pages.GetTreeAsync() ?? [];
        var named = new PageTokenIndex(live.Select(n => (n.Id, n.Path))).PathsFor(tokens);

        var joined = SearchScope.Normalise(string.Join(',', paths.Concat(named)));
        return new ResolvedSearchScope(joined, joined is null);
    }
}
