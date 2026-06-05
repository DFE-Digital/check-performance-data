using DfE.CheckPerformanceData.Web.Admin.Nav;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<IAdminNavEntry, CmsAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, SystemAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, VersionRetentionNavEntry>();
        services.AddSingleton<IAdminNavEntry, ContentStagingImportExportNavEntry>();
        services.AddSingleton<IAdminNavEntry, DeletedPagesNavEntry>();
        services.AddSingleton<IAdminNavEntry, SeedSamplePagesNavEntry>();
        services.AddSingleton<IAdminNavEntry, CmsSettingsNavEntry>();
        services.AddSingleton<IAdminNavEntry, VisualRegressionNavEntry>();
        services.AddSingleton<IAdminNavEntry, RulesEngineNavEntry>();
        services.AddSingleton<IAdminNavEntry, RulesConfigNavEntry>();
        services.AddSingleton<IAdminNavEntry, StorageAdminGroupNavEntry>();
        services.AddSingleton<IAdminNavEntry, StorageBrowserNavEntry>();
        return services;
    }
}
