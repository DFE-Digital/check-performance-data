namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Live admin nav entry that posts to the existing /help/seed endpoint to seed a small
// set of sample wiki pages. HttpMethod is POST so the landing-page view renders this
// tile as a form-button rather than an anchor; double-click prevention is wired in the
// view markup.
public sealed record SeedSamplePagesNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.SeedSamplePages;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Seed sample pages";
    public string Description => "Add a small set of sample wiki pages for testing and demonstration.";
    public string Url => string.Empty;
    public string HttpMethod => "POST";
    public bool Enabled => true;
    public int Order => 40;
}
