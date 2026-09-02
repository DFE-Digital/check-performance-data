namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Blob storage browser. It sits under the Danger zone group, not a storage group of its own:
// the browser can delete blobs, so it belongs with the other destructive tiles. Unlike Reset
// seed data it is registered in every environment — it is a live-support surface — so on
// Production the Danger zone group holds this tile alone.
public sealed record StorageBrowserNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.StorageBrowser;
    public string? ParentKey => AdminNavKeys.DangerZone;
    public string Title => "Blob storage browser";
    public string Description => "List containers, browse blobs, preview content, download, and delete.";
    public string Url => "/admin/storage";
    public bool Enabled => true;
    public int Order => 20;
}
