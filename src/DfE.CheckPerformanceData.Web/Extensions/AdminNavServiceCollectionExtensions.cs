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
    // includeDangerZone gates the destructive "Danger zone" group + tile. Program.cs passes
    // !IsProduction() so the reset action is never registered (and so never reachable) in prod.
    public static IServiceCollection AddAdminNavEntries(this IServiceCollection services, bool includeDangerZone = false)
    {
        // Some entries may resolve state from configuration. Provide an empty fallback so a bare
        // service collection (e.g. in registry tests) can still resolve the entries; the host's own
        // IConfiguration registration wins when present.
        services.TryAddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddSingleton<IAdminNavEntry, DashboardNavEntry>();
        services.AddSingleton<IAdminNavEntry, CmsAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, SystemAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, MessagesGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, AmendmentRequestsAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, UncommittedRequestsNavEntry>();
        services.AddSingleton<IAdminNavEntry, ContentStagingImportExportNavEntry>();
        services.AddSingleton<IAdminNavEntry, ContentPagesNavEntry>();
        services.AddSingleton<IAdminNavEntry, ContentBlocksNavEntry>();
        services.AddSingleton<IAdminNavEntry, DeletedPagesNavEntry>();
        services.AddSingleton<IAdminNavEntry, SearchAdminNavEntry>();
        services.AddSingleton<IAdminNavEntry, MessagesInboxNavEntry>();
        services.AddSingleton<IAdminNavEntry, SeedSamplePagesNavEntry>();
        services.AddSingleton<IAdminNavEntry, SeedSampleSearchDataNavEntry>();
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
        services.AddSingleton<IAdminNavEntry, StorageAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, StorageBrowserNavEntry>();
        services.AddSingleton<IAdminNavEntry, WindowAdminNavEntry>();
        services.AddSingleton<IAdminNavEntry, NewWindowNavEntry>();
        services.AddSingleton<IAdminNavEntry, ManageWindowNavEntry>();
        services.AddSingleton<IAdminNavEntry, TransactionsNavEntry>();
        services.AddSingleton<IAdminNavEntry, ReplaySubmissionsNavEntry>();

        if (includeDangerZone)
        {
            services.AddSingleton<IAdminNavEntry, DangerZoneGroupNavEntry>();
            services.AddSingleton<IAdminNavEntry, ResetSeedDataNavEntry>();
        }

        return services;
    }
}
