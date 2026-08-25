namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav tile that opens the seed-sample-search-data form. The controller serves
// that page only in Development, Review and QA and returns NotFound everywhere else, so
// this entry is registered only for those environments too (see AddAdminNavEntries) —
// otherwise a Preproduction or Production admin sees a tile that dead-ends in a 404.
// Gated by the parent group's section grant so admins reach it out of the box. Renders as
// a GET tile so the sidebar shows a regular anchor — the actual seed action is a POST
// submitted from the form.
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
