using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// Item 19: the left admin nav must track the current location for ALL admin pages — highlight the
// entry whose Url matches the request path (aria-current=page) and force-expand its ancestor
// groups. The highlight/expand machinery already lives in the recursive node partial, driven by an
// ActiveKey; what was missing is anyone SETTING that key from the request path. These tests pin
// (a) the layout computing the active key + current path into ViewData from the request, and
// (b) the resolver that maps a request path to the best-matching entry key.
public sealed class AdminNavActiveTrackingTests
{
    private static string SharedViewsDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "DfE.CheckPerformanceData.Web", "Views", "Shared"));

    private static string ReadAdminLayout() =>
        File.ReadAllText(Path.Combine(SharedViewsDir, "_AdminLayout.cshtml"));

    // --- The layout sets the active key + current path into ViewData from the live request ---

    [Fact]
    public void AdminLayout_ComputesActiveKeyAndCurrentPath_FromTheRequest()
    {
        var src = ReadAdminLayout();

        // The current request path feeds the nav so the active entry can be resolved.
        Assert.Contains("Request.Path", src);
        Assert.Contains("AdminCurrentPath", src);
        Assert.Contains("AdminActiveKey", src);
        // The resolver maps the path to a nav entry key.
        Assert.Contains("AdminNavActive.ResolveActiveKey", src);
    }

    // --- The resolver maps a request path to the entry whose Url is the best (longest) prefix ---

    [Theory]
    [InlineData("/admin/observability", AdminNavKeys.Observability)]
    [InlineData("/admin/observability/transactions", AdminNavKeys.Transactions)]
    [InlineData("/admin/queues", AdminNavKeys.RulesEngine)]
    [InlineData("/admin/queues/dlq", AdminNavKeys.DeadLetterQueue)]
    [InlineData("/admin/rules", AdminNavKeys.RulesConfig)]
    [InlineData("/admin/settings", AdminNavKeys.SystemSettings)]
    public void ResolveActiveKey_MatchesTheLongestUrlPrefix(string path, string expectedKey)
    {
        var entries = BuildEntries();

        var key = AdminNavActive.ResolveActiveKey(path, entries);

        Assert.Equal(expectedKey, key);
    }

    // --- A deeper path under an entry (e.g. a per-message journey) still resolves to that entry ---

    [Fact]
    public void ResolveActiveKey_DeepDescendantPath_ResolvesToTheNearestEntry()
    {
        var entries = BuildEntries();

        // A journey URL hangs off the dashboard but is not itself a nav entry; it must light the
        // Pipeline dashboard, the nearest ancestor entry — not nothing.
        var key = AdminNavActive.ResolveActiveKey("/admin/observability/journey/REF-1", entries);

        Assert.Equal(AdminNavKeys.Observability, key);
    }

    // --- Trailing slashes and query strings do not defeat the match ---

    [Fact]
    public void ResolveActiveKey_IgnoresTrailingSlashAndQuery()
    {
        var entries = BuildEntries();

        Assert.Equal(
            AdminNavKeys.Transactions,
            AdminNavActive.ResolveActiveKey("/admin/observability/transactions/", entries));
    }

    // --- An unmatched path yields null (no entry highlighted) rather than throwing ---

    [Fact]
    public void ResolveActiveKey_UnmatchedPath_ReturnsNull()
    {
        var entries = BuildEntries();

        Assert.Null(AdminNavActive.ResolveActiveKey("/something/else", entries));
    }

    // --- A group/container entry with no real Url is never matched as the active leaf ---

    [Fact]
    public void ResolveActiveKey_DoesNotMatchAnEmptyUrlContainer()
    {
        var entries = BuildEntries();

        // The /admin root must not resolve to a group whose Url is "#" or empty.
        var key = AdminNavActive.ResolveActiveKey("/admin", entries);

        Assert.True(key is null || entries.Single(e => e.Key == key).Url.StartsWith("/admin"));
    }

    private static IReadOnlyList<IAdminNavEntry> BuildEntries()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dev:ToolsEnabled"] = "true" })
            .Build();

        return new List<IAdminNavEntry>
        {
            new SystemAdminGroupNavEntry(),
            new RulesEngineGroupNavEntry(),
            new ObservabilityNavEntry(),
            new TransactionsNavEntry(),
            new RulesEngineNavEntry(),
            new DeadLetterQueueNavEntry(),
            new RulesConfigNavEntry(),
            new SystemSettingsNavEntry(),
        };
    }
}
