namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Top-level view model for the /admin landing page. Wraps the recursive nav forest: each
// root is a top-level group (rendered as a <section> with an <h2>), and the view recurses
// into descendants so that an empty-URL container (e.g. the Rules Engine sub-group) renders
// as a non-link sub-heading with its child pages listed as links beneath it. Roots and their
// descendants are pre-sorted by Order ascending (see AdminNavNodeViewModel.BuildForest).
public sealed class AdminLandingViewModel
{
    public IReadOnlyList<AdminNavNodeViewModel> Roots { get; init; } = [];
}
