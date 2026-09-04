using DfE.CheckPerformanceData.Web.Admin.Nav;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DfE.CheckPerformanceData.Web.Extensions;

// Registers the pre-seeded set of admin landing-page entries as singletons keyed off
// IAdminNavEntry. AdminController injects IEnumerable<IAdminNavEntry> and partitions
// them into groups (ParentKey is null) and tiles (ParentKey == group.Key). Future
// feature-area phases either edit an existing entry record in place (flipping Enabled
// and supplying Url) or register additional implementations from their own composition
// root.
public static class AdminNavServiceCollectionExtensions
{
    // includeResetSeedData gates the "Reset seed data" tile alone. Program.cs passes
    // !IsProduction() so the wipe-and-reseed action is never registered (and so never
    // reachable) in prod. The Danger zone group and the blob storage browser under it are
    // registered everywhere — the browser is a live-support surface, and the group renders
    // wherever at least one of its tiles survives FilterByAccess.
    //
    // includeSampleSearchData gates the "Seed sample search data" tile against the same
    // environment whitelist TestDataController enforces (Development / Review / QA).
    // Registering it everywhere put a tile in the Preproduction and Production sidebars
    // that only ever led to a 404. Both default to false: a missing tile is recoverable,
    // a dead link on a customer-facing environment is not.
    public static IServiceCollection AddAdminNavEntries(
        this IServiceCollection services,
        bool includeResetSeedData = false,
        bool includeSampleSearchData = false)
    {
        // Some entries may resolve state from configuration. Provide an empty fallback so a bare
        // service collection (e.g. in registry tests) can still resolve the entries; the host's own
        // IConfiguration registration wins when present.
        services.TryAddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddSingleton<IAdminNavEntry, DashboardNavEntry>();
        services.AddSingleton<IAdminNavEntry, CmsAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, SystemAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, MessagesGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, ContentStagingImportExportNavEntry>();
        services.AddSingleton<IAdminNavEntry, ContentPagesNavEntry>();
        services.AddSingleton<IAdminNavEntry, ContentBlocksNavEntry>();
        services.AddSingleton<IAdminNavEntry, DeletedPagesNavEntry>();
        services.AddSingleton<IAdminNavEntry, SearchAdminNavEntry>();
        services.AddSingleton<IAdminNavEntry, MessagesInboxNavEntry>();
        services.AddSingleton<IAdminNavEntry, SeedSamplePagesNavEntry>();
        services.AddSingleton<IAdminNavEntry, TestDataGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, SystemSettingsNavEntry>();
        services.AddSingleton<IAdminNavEntry, RoleSettingsNavEntry>();
        services.AddSingleton<IAdminNavEntry, AppLogsNavEntry>();
        services.AddSingleton<IAdminNavEntry, RulesConfigNavEntry>();
        services.AddSingleton<IAdminNavEntry, RulesEngineGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, RulesEngineNavEntry>();
        services.AddSingleton<IAdminNavEntry, RulesEngineQueueNavEntry>();
        services.AddSingleton<IAdminNavEntry, ZendeskQueueNavEntry>();
        services.AddSingleton<IAdminNavEntry, DeadLetterQueueNavEntry>();
        services.AddSingleton<IAdminNavEntry, ObservabilityNavEntry>();
        services.AddSingleton<IAdminNavEntry, WindowAdminNavEntry>();
        services.AddSingleton<IAdminNavEntry, NewWindowNavEntry>();
        services.AddSingleton<IAdminNavEntry, ManageWindowNavEntry>();
        services.AddSingleton<IAdminNavEntry, TransactionsNavEntry>();
        services.AddSingleton<IAdminNavEntry, ReplaySubmissionsNavEntry>();
        services.AddSingleton<IAdminNavEntry, DangerZoneGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, StorageBrowserNavEntry>();

        if (includeSampleSearchData)
        {
            services.AddSingleton<IAdminNavEntry, SeedSampleSearchDataNavEntry>();
        }

        if (includeResetSeedData)
        {
            services.AddSingleton<IAdminNavEntry, ResetSeedDataNavEntry>();
        }

        return services;
    }
}
