namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// View model for the Include journey's "already included" warning page. Shown when the typed
/// search entry on the Include select-pupil step matched a pupil already on the included list.
/// Carries the typed pupil label for display only — it is PII and must never be logged.
/// </summary>
public sealed class IncludeAlreadyIncludedViewModel
{
    public Guid WindowId { get; set; }

    /// <summary>The name the user typed. Display only — never logged or placed in analytics.</summary>
    public string? TypedPupilLabel { get; set; }
}