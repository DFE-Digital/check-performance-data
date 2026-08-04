namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav entry that posts to /admin/pages/sample-seed to seed a small set of
// published content pages under each of the four default root nodes. HttpMethod is POST so
// the landing-page view renders this tile as a form-button rather than an anchor; double-click
// prevention is wired in the view markup. ParentKey moved from CMS admin to the Test data
// sub-group so both seed tiles cluster together; the route + controller action stay put so
// existing bookmarks and the E2E fixture seed helper keep working.
public sealed record SeedSamplePagesNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.SeedSamplePages;
    public string? ParentKey => AdminNavKeys.TestDataGroup;
    public string Title => "Seed sample CMS pages";
    public string Description =>
        "Add a handful of published sample content pages under /wiki, /help, /support and /guidance for testing and demonstration.";
    public string Url => "/admin/pages/sample-seed";
    public string HttpMethod => "POST";
    public bool Enabled => true;
    public int Order => 10;
}
