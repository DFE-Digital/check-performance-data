namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

using DfE.CheckPerformanceData.Application.CheckYourPupilData;

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

    /// <summary>The name the user typed. Display only — never logged or placed in analytics.</summary>
    public string? TypedPupilLabel { get; set; }

    /// <summary>The pupils found on the included list matching the typed entry. Display only —
    /// never logged or placed in analytics. Empty when no match details were carried.</summary>
    public IReadOnlyList<PupilSuggestionDto> Matches { get; set; } = [];
}