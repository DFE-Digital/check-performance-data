namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public sealed record StorageBrowserNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.StorageBrowser;
    public string? ParentKey => AdminNavKeys.StorageAdmin;
    public string Title => "Blob storage browser";
    public string Description => "List containers, browse blobs, preview content, download, and delete.";
    public string Url => "/admin/storage";
    public bool Enabled => true;
    public int Order => 10;
}
