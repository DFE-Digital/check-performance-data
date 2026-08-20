using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.Controllers;

/// <summary>
/// The autocomplete source for the "which of {pupil}'s results is incorrect?" page (AB#296648).
///
/// The pupil is read from the session, never from the query string. Taking a pupil id from the
/// request would let a signed-in user at one school enumerate results for any pupil id they could
/// guess; the session already holds the pupil the journey selected, and that is the only scope this
/// endpoint will search.
/// </summary>
public sealed class ResultSuggestionsController(
    IStudentResultsClient results,
    ICurrentUserService currentUser) : Controller
{
    // Matches PupilSuggestionsController: below two characters a search is noise, and an
    // over-long query is not a real search.
    private const int MinQueryLength = 2;
    private const int MaxQueryLength = 100;

    [HttpGet]
    [Route("/results/suggestions")]
    public async Task<IActionResult> Suggestions(Guid windowId, string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < MinQueryLength || query.Length > MaxQueryLength)
            return Json(Array.Empty<object>());

        var journey = HttpContext.Session.GetRequestState(windowId);
        var cypmdId = journey.SelectedPupil?.Cypmd_Id;
        if (string.IsNullOrWhiteSpace(cypmdId))
            return Json(Array.Empty<object>());

        var held = await results.GetResultsAsync(windowId, currentUser.OrganisationLaestab, cypmdId, ct);

        return Json(held
            .Where(r => Matches(r, query))
            // The composite key, not the QAN: a pupil can hold the same qualification across
            // sessions and across source files, and the server re-resolves this key on POST.
            .Select(r => new { value = r.CompositeKey, label = ResultLabel.For(r) })
            .ToArray());
    }

    // Qualification name matches anywhere (users type a subject, e.g. "French", not a full title);
    // QAN matches on prefix only, since a substring match on an 8-digit code makes almost any
    // numeric query match everything.
    private static bool Matches(StudentResultRecord result, string query)
        => result.QualificationName.Contains(query, StringComparison.OrdinalIgnoreCase)
           || result.Qan.StartsWith(query, StringComparison.OrdinalIgnoreCase);

}
