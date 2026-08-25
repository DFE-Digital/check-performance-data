using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public sealed class ContentBlockSearchService(
    IContentBlockRepository repository,
    IPageNodeRepository pageNodeRepository,
    IHtmlRenderingService htmlRenderingService) : IContentBlockSearchService
{
    public async Task<ContentBlockSearchOutcome> SearchAsync(string? query, int max = 20)
    {
        var term = (query ?? string.Empty).Trim();
        if (term.Length < 2) return new ContentBlockSearchOutcome([], []);

        // Over-fetch so downstream URL-folding still yields up to `max` distinct URLs
        // when the incoming block set has many hits on the same page.
        var blocks = await repository.SearchAsync(term, max * 3);

        var results = new List<ContentBlockSearchResultDto>();
        var exclusions = new List<FilterExclusion>();

        foreach (var block in blocks)
        {
            // Repository-tier exclusions ride the widened projection: the row matched the
            // tsquery but the SQL-tier silent filter dropped it. Surface the breadcrumb; the
            // row does not participate in dedup or the kept-count cap.
            if (block.ExcludedBy is not null)
            {
                exclusions.Add(new FilterExclusion("block", block.ExcludedBy, block.Key));
                continue;
            }

            var path = block.LastSeenPath;
            // Never rendered anywhere: hide from search — the previous static-map fallback
            // pointed these at a URL that no longer existed, producing 404 links. No filter
            // slug exists for this branch (nothing dropped it — the row simply has no target
            // URL), so we skip silently rather than emit an exclusion breadcrumb.
            if (string.IsNullOrEmpty(path)) continue;

            // Admin / editor renders are not user-facing destinations.
            if (path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase))
            {
                exclusions.Add(new FilterExclusion("block", "admin-path", block.Key));
                continue;
            }

            var pageTitle = await ResolvePublishedPageTitleAsync(path);
            // The path resolves to something publicly linkable if either it matches a
            // currently-published PageNode (title from the node) or it's a static route
            // baked into a Razor view (title derived from the last segment). If neither,
            // we drop it: better to hide the block than to link to a 404.
            if (pageTitle is null)
            {
                exclusions.Add(new FilterExclusion("block", "unpublished-target", block.Key));
                continue;
            }

            var plain = htmlRenderingService.StripTagsToPlainText(
                htmlRenderingService.RenderHtml(block.Value));

            results.Add(new ContentBlockSearchResultDto
            {
                Key = block.Key,
                Url = path,
                PageTitle = pageTitle,
                SnippetHtml = BuildSnippet(plain, term),
                Rank = block.Rank,
                RankKeywords = block.RankKeywords,
                RankValue = block.RankValue,
            });

            // Cap semantics: this now bounds "kept blocks" rather than "distinct URLs kept".
            // URL-level dedup lives downstream in the canonicaliser — multiple blocks on the
            // same page all pass through here.
            if (results.Count >= max) break;
        }

        return new ContentBlockSearchOutcome(results, exclusions);
    }

    private async Task<string?> ResolvePublishedPageTitleAsync(string path)
    {
        // LastSeenPath is a request path ("/help/foo") — it comes straight from
        // HttpContext.Request.Path. PageNode.Path is stored without the leading slash
        // ("help/foo"), so the lookup has to be normalised or it can never match and every
        // block on a live CMS page gets dropped as "unpublished-target".
        var published = await pageNodeRepository.GetPublishedByPathAsync(path.TrimStart('/'));
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
