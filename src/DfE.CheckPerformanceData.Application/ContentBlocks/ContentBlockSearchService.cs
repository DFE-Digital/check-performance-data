using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public sealed class ContentBlockSearchService(
    IContentBlockRepository repository,
    IPageNodeRepository pageNodeRepository,
    IHtmlRenderingService htmlRenderingService) : IContentBlockSearchService
{
    public async Task<List<ContentBlockSearchResultDto>> SearchAsync(string? query, int max = 20)
    {
        var term = (query ?? string.Empty).Trim();
        if (term.Length < 2) return [];

        // Over-fetch so de-duplication by page still yields up to `max` results.
        var blocks = await repository.SearchAsync(term, max * 3);

        var results = new List<ContentBlockSearchResultDto>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in blocks)
        {
            var path = block.LastSeenPath;
            // Never rendered anywhere: hide from search — the previous static-map fallback
            // pointed these at a URL that no longer existed, producing 404 links.
            if (string.IsNullOrEmpty(path)) continue;
            // Admin / editor renders are not user-facing destinations.
            if (path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase)) continue;

            if (!seenUrls.Add(path)) continue;

            var pageTitle = await ResolvePublishedPageTitleAsync(path);
            // The path resolves to something publicly linkable if either it matches a
            // currently-published PageNode (title from the node) or it's a static route
            // baked into a Razor view (title derived from the last segment). If neither,
            // we drop it: better to hide the block than to link to a 404.
            if (pageTitle is null) continue;

            var plain = htmlRenderingService.StripTagsToPlainText(
                htmlRenderingService.RenderHtml(block.Value));

            results.Add(new ContentBlockSearchResultDto
            {
                Key = block.Key,
                Url = path,
                PageTitle = pageTitle,
                SnippetHtml = BuildSnippet(plain, term),
                Rank = block.Rank,
            });

            if (results.Count >= max) break;
        }

        return results;
    }

    private async Task<string?> ResolvePublishedPageTitleAsync(string path)
    {
        var published = await pageNodeRepository.GetPublishedByPathAsync(path);
        if (published is not null) return published.Title;

        // Static Razor routes (e.g. "/", "/check-your-pupil-data") aren't in PageNodes but are
        // always live in the app. Accept a small whitelist and derive a title from the trailing
        // segment. Anything else is unrecognised and dropped.
        return StaticRouteTitle(path);
    }

    private static string? StaticRouteTitle(string path)
    {
        if (path == "/") return "Home";
        return StaticRouteTitles.TryGetValue(path, out var title) ? title : null;
    }

    // Kept small and explicit: adding a page to the app means adding it here too, which is
    // easier to reason about than blindly emitting any path we haven't seen fail.
    private static readonly Dictionary<string, string> StaticRouteTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/check-your-pupil-data"] = "Check your pupil data",
        ["/guidance"] = "CYPMD help and support",
    };

    // Windowed HTML-encode + <mark>-wrap logic lives on the shared SearchSnippet helper.
    private static string BuildSnippet(string? plainText, string term) =>
        SearchSnippet.BuildWindow(plainText ?? string.Empty, term);
}
