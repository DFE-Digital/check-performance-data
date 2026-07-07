namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav entry that posts to /admin/pages/sample-seed to seed a small set of
// published content pages under each of the four default root nodes. HttpMethod is POST so
// the landing-page view renders this tile as a form-button rather than an anchor; double-click
// prevention is wired in the view markup.
public sealed record SeedSamplePagesNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.SeedSamplePages;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Seed sample pages";
    public string Description =>
        "Add a handful of published sample pages under /wiki, /help, /support and /guidance for testing and demonstration.";
    public string Url => "/admin/pages/sample-seed";
    public string HttpMethod => "POST";
    public bool Enabled => true;
    public int Order => 50;
}
