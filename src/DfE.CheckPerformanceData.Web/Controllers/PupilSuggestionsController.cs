using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class PupilSuggestionsController(ICheckYourPupilDataService service) : Controller
{
    [Route("/pupils/suggestions")]
    // requireResults is set by the PupilSearch page when its flow config asks for it, and limits
    // the search to students the school holds a result for. It is a search restriction only —
    // never a permission — so a caller that omits or forges it can still reach no pupil the
    // signed-in school's own file does not already contain.
    public async Task<IActionResult> Suggestions(Guid windowId, string? query, PupilFilter filter, Guid? excludePupilId, bool requireResults = false)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2 || query.Length > 100)
            return Json(Array.Empty<object>());

        var suggestions = await service.GetPupilSuggestionsAsync(windowId, query, filter, excludePupilId, requireResults);
        return Json(suggestions.Select(s => new { id = s.Id, label = s.Label }));
    }
}
