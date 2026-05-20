namespace DfE.CheckPerformanceData.Web.Admin.Nav;

// Registry contract for entries shown on the /admin landing page. Each implementation
// declares its display title, supporting description, target URL, an Enabled flag (set
// to false until the owning feature ships), and an Order for sort placement. Registered
// via AddAdminNavEntries and injected as IEnumerable<IAdminNavEntry> into AdminController.
public interface IAdminNavEntry
{
    string Title { get; }
    string Description { get; }
    string Url { get; }
    bool Enabled { get; }
    int Order { get; }
}
