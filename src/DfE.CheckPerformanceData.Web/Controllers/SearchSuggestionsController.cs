using DfE.CheckPerformanceData.Application.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

// Typeahead behind the content-page search widget's instant-search option. Anonymous, like
// /search itself, and reachable only as JSON.
//
// The payload is {label, url} rather than the {id, label} / {value, label} shapes the
// journey-domain suggestion endpoints use, because choosing a suggestion here navigates to a
// page instead of filling a hidden code field.
//
// Guards mirror /search: a term is trimmed then hard-sliced to the leading 100 characters, and
// anything shorter than the search service's own minimum never reaches the database at all —
// this runs on every keystroke, so the cheap rejection has to happen before the round trip.
[AllowAnonymous]
public sealed class SearchSuggestionsController(ISiteSearchService searchService) : Controller
{
    private const int MaxQueryLength = 100;
    private const int MinQueryLength = 2;
    private const int MaxSuggestions = 10;

    [HttpGet("/search/suggestions")]
    public async Task<IActionResult> Suggestions(string? q, string? scope)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length > MaxQueryLength) term = term[..MaxQueryLength];

        if (term.Length < MinQueryLength)
        {
            return Json(Array.Empty<SiteSearchSuggestion>());
        }

        var suggestions = await searchService.SuggestAsync(
            new SiteSearchSuggestQuery(term, scope, MaxSuggestions));

        return Json(suggestions);
    }
}
