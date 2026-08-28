using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// Which <see cref="JourneyController"/> action serves a page type.
///
/// Most page types share the generic <c>Page</c> action. The two search pages have their own routes
/// because their POST payloads differ from a question page's. Centralised because a back link that
/// guesses the wrong action 404s, and there are now three call sites (the page, pupil-search and
/// result-search back links) that all need the same answer.
/// </summary>
public static class JourneyRouting
{
    public static string ActionFor(PageType? pageType) => pageType switch
    {
        PageType.PupilSearch => nameof(JourneyController.PupilSearchPage),
        PageType.ResultSearch => nameof(JourneyController.ResultSearchPage),
        // AB#297848: resolves an AO+QAN pair server-side, the same reason ResultSearch has its own action.
        PageType.QualificationSearch => nameof(JourneyController.QualificationSearchPage),
        // ResultDetails renders its own view but is otherwise a question page, so it is served by
        // the generic Page action — the same arrangement EvidenceUpload uses.
        _ => nameof(JourneyController.Page)
    };
}
