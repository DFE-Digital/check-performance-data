namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Group descriptor: top-level Amendment requests section. ParentKey is null so the
// landing-page controller treats it as a group container. Url is empty because it is a
// container, not a navigable page.
public sealed record AmendmentRequestsAdminGroupNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.AmendmentRequestsAdmin;
    public string? ParentKey => null;
    public string Title => "Amendment requests";
    public string Description => "Submitted change requests and their rules-engine outcomes.";
    public string Url => string.Empty;
    public bool Enabled => true;
    public int Order => 15;
}
