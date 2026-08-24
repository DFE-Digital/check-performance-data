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

    public IReadOnlyList<string> AwardingOrganisations { get; set; } = [];

    /// <summary>Every qualification in the reference document, AO-grouped for the optgroup markup.</summary>
    public IReadOnlyList<QualificationReference> Qualifications { get; set; } = [];

    public string? SelectedAo { get; set; }
    public string? SelectedQan { get; set; }

    public string? BackPageId { get; set; }

    /// <summary>The JourneyController action that serves <see cref="BackPageId"/>.</summary>
    public string BackPageAction { get; set; } = nameof(JourneyController.Page);
}
