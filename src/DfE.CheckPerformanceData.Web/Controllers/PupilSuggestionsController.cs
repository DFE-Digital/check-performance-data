using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class PupilSuggestionsController(ICheckYourPupilDataService service) : Controller
{
    [Route("/pupils/suggestions")]
    // requireResults and pupilSearchField are set by the PupilSearch page when its flow config asks
    // for them, and each limits what the search can return. They are search restrictions only —
    // never permissions — so a caller that omits or forges one can still reach no pupil the
    // signed-in school's own file does not already contain.
    //
    // The parameter names below are the query-string contract: PupilSearch.cshtml sends these keys
    // and docs/request-journey.md line 132 documents them. Renaming one silently drops the
    // restriction, because model binding ignores an unrecognised key and the default then applies —
    // AB#304118's CYPMD-ID-only page searched by name again with no error. PupilSearchViewSourceTests
    // reflects this signature against the keys the view emits, so the two cannot drift apart again.
    public async Task<IActionResult> Suggestions(Guid windowId, string? query, PupilFilter filter, Guid? excludePupilId, bool requireResults = false, PupilSearchField pupilSearchField = PupilSearchField.All)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2 || query.Length > 100)
            return Json(Array.Empty<object>());

        var suggestions = await service.GetPupilSuggestionsAsync(windowId, query, filter, excludePupilId, requireResults, pupilSearchField);
        return Json(suggestions.Select(s => new { id = s.Id, label = s.Label }));
    }
}
