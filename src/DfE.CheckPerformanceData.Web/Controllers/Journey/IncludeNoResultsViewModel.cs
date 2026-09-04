namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// View model for the Include journey's "Pupil not found" page. Shown when the typed search
/// entry on the Include select-pupil step matched no pupil on either the included or the
/// non-included list. Carries the typed pupil label for display only — it is PII and must
/// never be logged.
/// </summary>
public sealed class IncludeNoResultsViewModel
{
    public Guid WindowId { get; set; }

    /// <summary>The Include select-pupil page id (target for the "Search again" action).</summary>
    public string PageId { get; set; } = string.Empty;

    /// <summary>The name the user typed. Display only — never logged or placed in analytics.</summary>
    public string? TypedPupilLabel { get; set; }
}