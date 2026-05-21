namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Top-level view model for the /admin landing page. Wraps the grouped list of nav
// entries; the view renders one <section> per group with the group's child tiles
// inside. Groups are pre-sorted by Order ascending.
public sealed class AdminLandingViewModel
{
    public IReadOnlyList<AdminNavGroupViewModel> Groups { get; init; } = [];
}
