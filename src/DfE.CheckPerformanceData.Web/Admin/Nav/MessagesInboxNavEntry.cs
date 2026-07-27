namespace DfE.CheckPerformanceData.Web.Admin.Nav;

public sealed record MessagesInboxNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.MessagesInbox;
    public string? ParentKey => AdminNavKeys.CmsAdmin;
    public string Title => "Search messages inbox";
    public string Description => "User feedback on search results — read messages and drill into the session's search history.";
    public string Url => "/admin/Messages/Inbox";
    public bool Enabled => true;
    public int Order => 70;
}
