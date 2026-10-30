namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// View model for the Include journey's "already included" warning page. Shown when the typed
/// search entry on the Include select-pupil step matched a pupil already on the included list.
/// Carries the typed pupil label for display only — it is PII and must never be logged. Also lists
/// the matching pupils found on the included list, so the page can show who was matched (also PII,
/// display only — never logged or placed in analytics).
/// </summary>
public sealed class IncludeAlreadyIncludedViewModel
{
    public Guid WindowId { get; set; }

    /// <summary>The pageId of the pupil search page to return to via the back link.</summary>
    public string? BackPageId { get; set; }

    /// <summary>The JourneyController action the Back link targets. Defaults to the Include pupil
    /// search page; the duplicate-check hand-off sets it to <see cref="JourneyController.DuplicateCheck"/>
    /// so "Back" returns to the match list the pupil was included from.</summary>
    public string BackPageAction { get; set; } = nameof(JourneyController.PupilSearchPage);

    /// <summary>The name the user typed. Display only — never logged or placed in analytics.</summary>
    public string? TypedPupilLabel { get; set; }

    /// <summary>The pupils found on the included list matching the typed entry. Display only —
    /// never logged or placed in analytics. Empty when no match details were carried.</summary>
    public IReadOnlyList<PupilSuggestionDto> Matches { get; set; } = [];

    /// <summary>The window's word for a learner: "student" on 16-19, "pupil" elsewhere.</summary>
    public LearnerNoun LearnerNoun { get; set; } = LearnerNoun.Pupil;

    /// <summary>True when the window type has an Add flow, so "Start adding" leads somewhere.
    /// 16-19 has none (#711).</summary>
    public bool CanAdd { get; set; }
}