namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Tile under Amendment requests: the read-only list of SubmittedUnCommitted change
// requests for the current open window and their rules-engine outcome.
public sealed record UncommittedRequestsNavEntry : IAdminNavEntry
{
    public string Key => AdminNavKeys.UncommittedRequests;
    public string? ParentKey => AdminNavKeys.AmendmentRequestsAdmin;
    public string Title => "Uncommitted requests";
    public string Description => "Submitted (uncommitted) requests for the current open window and their rules-engine outcome.";
    public string Url => "/admin/uncommitted-requests";
    public bool Enabled => true;
    public int Order => 10;
}
