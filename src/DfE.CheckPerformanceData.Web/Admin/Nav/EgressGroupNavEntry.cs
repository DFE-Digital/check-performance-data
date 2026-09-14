namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public sealed record EgressGroupNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.EgressGroup;
    public string? ParentKey => null;
    public string Title => "Data egress";
    public string Description => "Pull approved amendment decisions from Zendesk, prepare them to the LDS specification and transfer them.";
    public string Url => string.Empty;
    public bool Enabled => true;
    public int Order => 35;
}
