using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// The "Provide the missing qualification details" AO + QAN page (AB#297848). Mirrors
/// <see cref="ResultSearchViewModel"/>: every option renders as a real element so the page works
/// with JavaScript off, with script narrowing the QAN list to the chosen AO.
/// </summary>
public sealed class QualificationSearchViewModel
{
    public Guid WindowId { get; set; }
    public string PageId { get; set; } = string.Empty;
    public JourneyPage Page { get; set; } = null!;

    public string PupilName { get; set; } = string.Empty;
    public string? CypmdId { get; set; }

    /// <summary>
    /// The page heading with <c>{pupilName}</c> substituted, or null when the flow config gives the
    /// page no title. Read from the config rather than hardcoded in the view so the copy the
    /// content team edits in MissingQualification_Post16.json is the copy that renders.
    /// </summary>
    public string? ResolvedTitle => string.IsNullOrEmpty(Page?.Title)
        ? null
        : Page.Title.Replace("{pupilName}", PupilName, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> AwardingOrganisations { get; set; } = [];

    /// <summary>Every qualification in the reference document, AO-grouped for the optgroup markup.</summary>
    public IReadOnlyList<QualificationReference> Qualifications { get; set; } = [];

    public string? SelectedAo { get; set; }
    public string? SelectedQan { get; set; }

    public string? BackPageId { get; set; }

    /// <summary>The JourneyController action that serves <see cref="BackPageId"/>.</summary>
    public string BackPageAction { get; set; } = nameof(JourneyController.Page);

    /// <summary>
    /// True when the user arrived from the check-answers page's "Change" link on the AO or QAN row.
    /// Carried through the form so re-confirming the same qualification returns them to the summary
    /// instead of marching them forward through the rest of the journey again (AB#297848) — the same
    /// contract PagePost honours for every question page.
    /// </summary>
    public bool FromSummary { get; set; }
}
