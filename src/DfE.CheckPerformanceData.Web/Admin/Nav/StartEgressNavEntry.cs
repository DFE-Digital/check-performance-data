namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public sealed record StartEgressNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.Egress;
    public string? ParentKey => AdminNavKeys.EgressGroup;
    public string Title => "Start a new egress";
    public string Description => "Choose a checking window and output types, pull the decisions and transfer the files to LDS. Saved runs are listed here too.";
    public string Url => "/admin/egress";
    public bool Enabled => true;
    public int Order => 10;
}
