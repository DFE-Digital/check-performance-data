namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Group descriptor: top-level Messages section. Collects the incoming-message surfaces —
// user-submitted search feedback and the dead-letter queue — under a single admin
// container so the top-nav badge can consolidate their counts into one indicator.
// ParentKey is null so the landing-page controller treats it as a group container. Url
// points at the group landing at /admin/Messages which lists its child tiles.
public sealed record MessagesGroupNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.MessagesGroup;
    public string? ParentKey => null;
    public string Title => "Messages";
    public string Description => "User-submitted search feedback and the dead-letter queue.";
    public string Url => "/admin/Messages";
    public bool Enabled => true;
    public int Order => 25;
}
