using DfE.CheckPerformanceData.Application.Countries;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class CountryController(ICountryService countryService) : Controller
{
    [Route("/Countries/suggestions")]
    public async Task<IActionResult> Suggestions(string? query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2 || query.Length > 100)
            return Json(Array.Empty<object>());

        var suggestions = await countryService.SearchAsync(query, cancellationToken);
        return Json(suggestions.Select(s => new { id = s.Code, label = s.Name }));
    }
}
