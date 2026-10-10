namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public sealed record ImpersonationNavEntry : IAdminNavEntry
{
    public string Key => "impersonation";
    public string? ParentKey => null;
    public string Title => "View as an establishment";
    public string Description => "View establishment data in read-only mode.";
    public string Url => "/admin/impersonation";
    public bool Enabled => true;
    public int Order => 45;
}
