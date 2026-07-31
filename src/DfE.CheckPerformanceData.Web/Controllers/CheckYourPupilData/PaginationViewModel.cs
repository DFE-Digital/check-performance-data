namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

/// <summary>
/// Pagination for one section. The query string is rebuilt from EVERY section's current page and
/// search term, so the other sections keep their state and adding a third section needs no code
/// change here.
/// </summary>
public sealed class PaginationViewModel
{
    public required string WindowId { get; init; }
    public required PupilTableSection Section { get; init; }
    public required IReadOnlyList<PupilTableSection> AllSections { get; init; }

    /// <summary>Anchor/tab to return to, e.g. "included".</summary>
    public string? TabAnchor { get; init; }
}
