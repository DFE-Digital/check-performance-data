using System.Net;
using DfE.CheckPerformanceData.Application.Common;

namespace DfE.CheckPerformanceData.Application.ContentBlocks;

public sealed class ContentBlockSearchService(
    IContentBlockRepository repository,
    IHtmlRenderingService htmlRenderingService) : IContentBlockSearchService
{
    public async Task<List<ContentBlockSearchResultDto>> SearchAsync(string? query, int max = 20)
    {
        var term = (query ?? string.Empty).Trim();
        if (term.Length < 2) return [];

        // Over-fetch so de-duplication by page/section still yields up to `max` results.
        var blocks = await repository.SearchAsync(term, max * 3);

        var results = new List<ContentBlockSearchResultDto>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in blocks)
        {
            var location = ContentBlockLocations.Resolve(block.Key);
            if (location is null) continue;
            if (!seenUrls.Add(location.Url)) continue;

            var plain = htmlRenderingService.StripTagsToPlainText(
                htmlRenderingService.RenderHtml(block.Value));

            results.Add(new ContentBlockSearchResultDto
            {
                Key = block.Key,
                Url = location.Url,
                PageTitle = location.PageTitle,
                SnippetHtml = BuildSnippet(plain, term)
            });

            if (results.Count >= max) break;
        }

        return results;
    }

    // Produces safe snippet HTML: everything is HTML-encoded, then the matched term is
    // wrapped in a <mark>. The only markup that can reach the page is that <mark>.
    private static string BuildSnippet(string? plainText, string term)
    {
        var plain = plainText ?? string.Empty;
        var idx = plain.IndexOf(term, StringComparison.OrdinalIgnoreCase);

        if (idx < 0)
        {
            var head = plain.Length <= 200 ? plain : plain[..200] + "…";
            return WebUtility.HtmlEncode(head);
        }

        var start = Math.Max(0, idx - 60);
        var length = Math.Min(plain.Length - start, 200);
        var window = plain.Substring(start, length);
        var wi = window.IndexOf(term, StringComparison.OrdinalIgnoreCase);

        var before = WebUtility.HtmlEncode(window[..wi]);
        var match = WebUtility.HtmlEncode(window.Substring(wi, term.Length));
        var after = WebUtility.HtmlEncode(window[(wi + term.Length)..]);

        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = start + length < plain.Length ? "…" : string.Empty;
        return $"{prefix}{before}<mark>{match}</mark>{after}{suffix}";
    }
}
