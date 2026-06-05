using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class PupilSuggestionsController(ICheckYourPupilDataService service) : Controller
{
    [Route("/pupils/suggestions")]
    public async Task<IActionResult> Suggestions(Guid windowId, string? query, PupilFilter filter, Guid? excludePupilId)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2 || query.Length > 100)
            return Json(Array.Empty<object>());

        var suggestions = await service.GetPupilSuggestionsAsync(windowId, query, filter, excludePupilId);
        return Json(suggestions.Select(s => new { id = s.Id, label = s.Label }));
    }
}
