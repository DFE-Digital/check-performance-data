namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav tile that opens the seed-sample-search-data form. The controller is
// dev-only (returns NotFound outside Development) and gated by the parent group's
// section grant so admins reach it out of the box. Renders as a GET tile so the sidebar
// shows a regular anchor — the actual seed action is a POST submitted from the form.
public sealed record SeedSampleSearchDataNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.SeedSampleSearchData;
    public string? ParentKey => AdminNavKeys.TestDataGroup;
    public string Title => "Seed sample search data";
    public string Description =>
        "Generate a plausible mix of search events and feedback messages across a chosen time span so the analytics dashboard has demo content.";
    public string Url => "/admin/test-data/sample-search-data";
    public bool Enabled => true;
    public int Order => 20;
}
