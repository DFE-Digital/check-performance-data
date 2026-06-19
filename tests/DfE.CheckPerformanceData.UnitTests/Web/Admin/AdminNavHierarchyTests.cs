using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// Pins the Phase 3.15 deep (3-4 level) admin nav IA under System administration:
//   Rules Engine
//     Pipeline dashboard
//       Debug Pipelines        (dev-gated)
//     Queues
//       Rules Engine Queue
//       Zendesk Queue
//       Dead Letter Queue
//     Rules Engine configuration
//   System settings
// Also guards removal of the Visual regression dashboard tile.
public sealed class AdminNavHierarchyTests
{
    private static IReadOnlyList<IAdminNavEntry> ResolveEntries()
    {
        var services = new ServiceCollection();
        services.AddAdminNavEntries();
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<IAdminNavEntry>().ToList();
    }

    private static IAdminNavEntry Single(string key) =>
        ResolveEntries().Single(e => e.Key == key);

    // --- RulesEngineGroup_Is_SystemAdmin_Child ---

    [Fact]
    public void RulesEngineGroup_Is_SystemAdmin_Child()
    {
        var group = Single(AdminNavKeys.RulesEngineGroup);

        Assert.Equal(AdminNavKeys.SystemAdmin, group.ParentKey);
        Assert.Equal("Rules Engine", group.Title);
        Assert.True(group.Enabled);
    }

    // --- PipelineDashboard_Is_RulesEngineGroup_Child ---

    [Fact]
    public void PipelineDashboard_Is_RulesEngineGroup_Child()
    {
        var dashboard = Single(AdminNavKeys.Observability);

        Assert.Equal(AdminNavKeys.RulesEngineGroup, dashboard.ParentKey);
        Assert.Equal("/admin/observability", dashboard.Url);
    }

    // --- DebugPipelines_Nav_Entry_Is_Removed ---
    // The standalone /dev/uat Debug Pipeline page was retired (its controls fold into the
    // dashboard's Demo panel), so its nav entry must be gone.

    [Fact]
    public void DebugPipelines_Nav_Entry_Is_Removed()
    {
        var entries = ResolveEntries();

        Assert.DoesNotContain(entries, e => e.Key == "debug-menu");
        Assert.DoesNotContain(entries, e => e.Url == "/dev/uat");
        Assert.DoesNotContain(entries, e => e.Title.Contains("Debug Pipeline", StringComparison.OrdinalIgnoreCase));
    }

    // --- Transactions_Sits_Under_PipelineDashboard ---

    [Fact]
    public void Transactions_Sits_Under_PipelineDashboard()
    {
        var transactions = Single(AdminNavKeys.Transactions);

        Assert.Equal(AdminNavKeys.Observability, transactions.ParentKey);
        Assert.Equal("Transactions", transactions.Title);
        Assert.Equal("/admin/observability/transactions", transactions.Url);
        Assert.True(transactions.Enabled);
    }

    // --- PipelineDashboard_Children_Is_Transactions ---

    [Fact]
    public void PipelineDashboard_Children_Is_Transactions()
    {
        var entries = ResolveEntries();

        var dashboardChildren = entries
            .Where(e => e.ParentKey == AdminNavKeys.Observability)
            .OrderBy(e => e.Order)
            .ToList();

        // The standalone Replay submissions page was retired (its picker now lives in the dashboard
        // Demo panel), so the dashboard parents just Transactions.
        Assert.Single(dashboardChildren);
        Assert.Equal(AdminNavKeys.Transactions, dashboardChildren[0].Key);
    }

    // --- Queues_Is_RulesEngineGroup_Child ---

    [Fact]
    public void Queues_Is_RulesEngineGroup_Child()
    {
        var queues = Single(AdminNavKeys.RulesEngine);

        Assert.Equal(AdminNavKeys.RulesEngineGroup, queues.ParentKey);
        Assert.Equal("Queues", queues.Title);
        Assert.Equal("/admin/queues", queues.Url);
    }

    // --- Three_Queue_Children_Sit_Under_Queues ---

    [Fact]
    public void Three_Queue_Children_Sit_Under_Queues()
    {
        var entries = ResolveEntries();

        var queueChildren = entries
            .Where(e => e.ParentKey == AdminNavKeys.RulesEngine)
            .OrderBy(e => e.Order)
            .ToList();

        Assert.Equal(3, queueChildren.Count);

        Assert.Equal(AdminNavKeys.RulesEngineQueue, queueChildren[0].Key);
        Assert.Equal("Rules Engine Queue", queueChildren[0].Title);
        Assert.Equal("/admin/queues/list/rules-engine", queueChildren[0].Url);

        Assert.Equal(AdminNavKeys.ZendeskQueue, queueChildren[1].Key);
        Assert.Equal("Zendesk Queue", queueChildren[1].Title);
        Assert.Equal("/admin/queues/list/zendesk", queueChildren[1].Url);

        Assert.Equal(AdminNavKeys.DeadLetterQueue, queueChildren[2].Key);
        Assert.Equal("Dead Letter Queue", queueChildren[2].Title);
        Assert.Equal("/admin/queues/dlq", queueChildren[2].Url);
    }

    // --- RulesConfig_Is_RulesEngineGroup_Child ---

    [Fact]
    public void RulesConfig_Is_RulesEngineGroup_Child()
    {
        var config = Single(AdminNavKeys.RulesConfig);

        Assert.Equal(AdminNavKeys.RulesEngineGroup, config.ParentKey);
        Assert.Equal("Rules Engine configuration", config.Title);
        Assert.Equal("/admin/rules", config.Url);
    }

    // --- SystemSettings_Stays_Top_Level_Under_SystemAdmin ---

    [Fact]
    public void SystemSettings_Stays_Top_Level_Under_SystemAdmin()
    {
        var settings = Single(AdminNavKeys.SystemSettings);

        Assert.Equal(AdminNavKeys.SystemAdmin, settings.ParentKey);
        Assert.Equal("/admin/settings", settings.Url);
    }

    // --- VisualRegression_Entry_Is_Removed ---

    [Fact]
    public void VisualRegression_Entry_Is_Removed()
    {
        var entries = ResolveEntries();

        Assert.DoesNotContain(entries, e => e.Key == "vr-dashboard");
        Assert.DoesNotContain(entries, e => e.Title.Contains("Visual regression", StringComparison.OrdinalIgnoreCase));
    }

    // --- Every_NonRoot_Entry_Has_A_Resolvable_Parent ---

    [Fact]
    public void Every_NonRoot_Entry_Has_A_Resolvable_Parent()
    {
        var entries = ResolveEntries();
        var keys = entries.Select(e => e.Key).ToHashSet();

        foreach (var entry in entries.Where(e => e.ParentKey is not null))
        {
            Assert.Contains(entry.ParentKey!, keys);
        }
    }
}
