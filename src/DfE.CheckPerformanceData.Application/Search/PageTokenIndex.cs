namespace DfE.CheckPerformanceData.Application.Search;

// Finds the pages a list of page tokens names, from the pages that currently exist. Built per
// request from the live page list, which is small, so nothing is stored or cached between
// requests and a renamed, moved or deleted page is reflected straight away.
//
// tokenFor is how a page's token is worked out; it is PageToken.For everywhere but tests, which
// replace it to give two pages the same token.
public sealed class PageTokenIndex(IEnumerable<(Guid Id, string Path)> pages, Func<Guid, string>? tokenFor = null)
{
    private readonly ILookup<string, string> _pathsByToken =
        pages.ToLookup(p => (tokenFor ?? PageToken.For)(p.Id), p => p.Path, StringComparer.Ordinal);

    // The paths of the pages the tokens name, in token order. An unknown token names nothing. A
    // token two pages share names both, so a clash can only widen a search, never hide a page.
    public IReadOnlyList<string> PathsFor(IEnumerable<string> tokens) =>
        tokens.SelectMany(t => _pathsByToken[t])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
