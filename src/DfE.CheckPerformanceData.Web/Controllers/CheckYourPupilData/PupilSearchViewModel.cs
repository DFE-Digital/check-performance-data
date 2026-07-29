namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

/// <summary>
/// The search box for one section. Every other section's page and search term ride along as
/// hidden inputs so searching one table does not reset the others.
/// </summary>
public sealed class PupilSearchViewModel
{
    public required string WindowId { get; init; }
    public required PupilTableSection Section { get; init; }
    public required IReadOnlyList<PupilTableSection> AllSections { get; init; }
    public string? ActiveTab { get; init; }
}
