using DfE.CheckPerformanceData.Web.Admin.Nav;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// A single admin nav group with its resolved child entries. Built by AdminController
// from the flat IEnumerable<IAdminNavEntry> registration by matching tiles whose
// ParentKey equals this group's Key. Children are pre-sorted by Order ascending.
public sealed class AdminNavGroupViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<IAdminNavEntry> Children { get; init; } = [];
}
