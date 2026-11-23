namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Synthetic IAdminNavEntry created at render time from a media library folder — NOT DI-registered.
// Grafted under the "Media library" entry, so the left-hand tree shows the folder tree and a
// folder opens the library in that folder. Access follows the Media section.
public sealed record MediaFolderNavEntry(Guid FolderId, string FolderName, string ParentNavKey) : IAdminNavEntry
{
    public string Key => $"media-folder-{FolderId:D}";
    public string? ParentKey => ParentNavKey;
    public string Title => FolderName;
    public string Description => string.Empty;
    public string Url => $"/admin/media?folder={FolderId:D}";
    public bool Enabled => true;
    public int Order => 0; // pre-sorted by MediaFolderTree
}
