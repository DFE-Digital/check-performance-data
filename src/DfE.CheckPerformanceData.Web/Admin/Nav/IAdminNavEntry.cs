namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Registry contract for entries shown on the /admin landing page. Each implementation
// declares a stable Key, an optional ParentKey (null for groups, non-null for tiles),
// a display title, supporting description, target URL, an Enabled flag (set to false
// until the owning feature ships), an Order for sort placement within its group, and
// the HTTP verb used by the tile's primary affordance (default GET; POST signals a
// form-button tile). Registered via AddAdminNavEntries and injected as
// IEnumerable<IAdminNavEntry> into AdminController.
public interface IAdminNavEntry
{
    string Key { get; }
    string? ParentKey { get; }
    string Title { get; }
    string Description { get; }
    string Url { get; }
    bool Enabled { get; }
    int Order { get; }
    string HttpMethod => "GET";
}
