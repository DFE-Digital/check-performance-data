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
    public static IServiceCollection AddAdminNavEntries(this IServiceCollection services)
    {
        // DebugMenuNavEntry resolves its Enabled state from configuration. Provide an empty
        // fallback so a bare service collection (e.g. in registry tests) can still resolve the
        // entries; the host's own IConfiguration registration wins when present.
        services.TryAddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddSingleton<IAdminNavEntry, CmsAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, SystemAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, VersionRetentionNavEntry>();
        services.AddSingleton<IAdminNavEntry, ContentStagingImportExportNavEntry>();
        services.AddSingleton<IAdminNavEntry, DeletedPagesNavEntry>();
        services.AddSingleton<IAdminNavEntry, SeedSamplePagesNavEntry>();
        services.AddSingleton<IAdminNavEntry, SystemSettingsNavEntry>();
        services.AddSingleton<IAdminNavEntry, RulesConfigNavEntry>();
        services.AddSingleton<IAdminNavEntry, RulesEngineGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, RulesEngineNavEntry>();
        services.AddSingleton<IAdminNavEntry, RulesEngineQueueNavEntry>();
        services.AddSingleton<IAdminNavEntry, ZendeskQueueNavEntry>();
        services.AddSingleton<IAdminNavEntry, DeadLetterQueueNavEntry>();
        services.AddSingleton<IAdminNavEntry, ObservabilityNavEntry>();
        services.AddSingleton<IAdminNavEntry, StorageAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, StorageBrowserNavEntry>();
        services.AddSingleton<IAdminNavEntry, DebugMenuNavEntry>();
        services.AddSingleton<IAdminNavEntry, TransactionsNavEntry>();
        services.AddSingleton<IAdminNavEntry, ReplaySubmissionsNavEntry>();
        return services;
    }
}
