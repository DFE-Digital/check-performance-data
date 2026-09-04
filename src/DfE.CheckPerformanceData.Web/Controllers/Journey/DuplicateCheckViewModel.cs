using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// AB#297780: the Add journey's pupil-duplicate warning page. Renders when the learner details
/// posted on the learner-details page match an existing pupil (included or non-included) so the
/// school can decide whether an already-known pupil really needs adding. Matches carry pupil PII
/// for display only — never written to logs or analytics.
/// </summary>
public sealed class DuplicateCheckViewModel
{
    public Guid WindowId { get; set; }

    /// <summary>The pageId of the page to return to via the back link.</summary>
    public string? BackPageId { get; set; }

    public required DuplicateScenario Scenario { get; init; }

    public required IReadOnlyList<DuplicateMatch> Matches { get; init; }

    /// <summary>Human label for the warned-about pupil: "Surname, Firstname".</summary>
    public string LearnerNameLabel { get; init; } = string.Empty;

    /// <summary>
    /// True only for a single non-included match (US2): the school may start the Include journey
    /// for that existing pupil instead of adding a fresh record. Never true for an already-included
    /// pupil or for a multiple-match list (those use per-row Switch-to-Include in US3).
    /// </summary>
    public bool IncludeActionAvailable =>
        Scenario == DuplicateScenario.SingleNonIncluded && Matches.Count == 1;
}
