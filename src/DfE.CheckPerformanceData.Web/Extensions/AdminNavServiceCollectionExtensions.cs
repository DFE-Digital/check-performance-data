using DfE.CheckPerformanceData.Web.Admin.Nav;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Web.Extensions;

// Registers the pre-seeded set of admin landing-page entries as singletons keyed off
// IAdminNavEntry. AdminController injects IEnumerable<IAdminNavEntry> and sorts by
// Order. Future feature-area phases either edit the existing entry record in place
// (flipping Enabled and supplying Url) or register additional implementations from
// their own composition root.
public static class AdminNavServiceCollectionExtensions
{
    public static IServiceCollection AddAdminNavEntries(this IServiceCollection services)
    {
        services.AddSingleton<IAdminNavEntry, VersionRetentionNavEntry>();
        services.AddSingleton<IAdminNavEntry, VisualRegressionNavEntry>();
        services.AddSingleton<IAdminNavEntry, ContentStagingImportNavEntry>();
        return services;
    }
}
