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
    /// #749: true when the window type has an Include flow (<c>Include_{CheckingWindowType}.json</c>),
    /// read from the same list What to change renders. False by default, so a page built without it
    /// offers no Include action (fail closed). KS4 Autumn has no Include flow.
    /// </summary>
    public bool IncludeJourneyAvailable { get; init; }

    /// <summary>
    /// True only for a single non-included match (US2) on a window type with an Include flow: the
    /// school may start the Include journey for that existing pupil instead of adding a fresh record.
    /// Never true for an already-included pupil or for a multiple-match list (those use per-row
    /// Switch-to-Include in US3).
    /// </summary>
    public bool IncludeActionAvailable =>
        IncludeJourneyAvailable && Scenario == DuplicateScenario.SingleNonIncluded && Matches.Count == 1;

    /// <summary>
    /// #749: true when the multiple-match list may offer "Switch to include" on its non-included
    /// rows (US3). The view still checks each row's <see cref="DuplicateMatch.IsIncluded"/>.
    /// </summary>
    public bool SwitchToIncludeAvailable =>
        IncludeJourneyAvailable && Scenario == DuplicateScenario.Multiple;

    // AB#297780 conflict state: set only when the Include hand-off found a submitted request for the
    // matched pupil (FR-010). Null on every non-conflict render, so the existing page is unaffected.
    public string? ConflictErrorReference { get; set; }
    public string? ConflictErrorLink { get; set; }
    public string? ConflictPupilName { get; set; }
    public string? ConflictReasonType { get; set; }
    public string? ConflictUserName { get; set; }
    public string? ConflictAttentionHtml { get; set; }
}
