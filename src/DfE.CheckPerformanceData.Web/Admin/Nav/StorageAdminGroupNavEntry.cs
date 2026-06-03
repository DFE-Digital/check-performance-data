namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public sealed record StorageAdminGroupNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.StorageAdmin;
    public string? ParentKey => null;
    public string Title => "Storage administration";
    public string Description => "Browse and manage Azure Blob Storage containers and blobs.";
    public string Url => string.Empty;
    public bool Enabled => true;
    public int Order => 30;
}
