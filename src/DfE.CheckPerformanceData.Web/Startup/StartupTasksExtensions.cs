using System.IO;
using DfE.CheckPerformanceData.Application.ContentStaging;
using System.Threading.Tasks;
using DfE.CheckPerformanceData.Application.PageTree;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Seeding;
using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.Web.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class StartupTasksExtensions
{
    public static async Task RunCpdStartupTasksAsync(this WebApplication app)
    {
        await app.MigrateDatabaseAsync();

        using (var scope = app.Services.CreateScope())
        {
            // Countries back the country autocomplete and must exist in every environment,
            // including Production. Seeded idempotently and content-aware: a no-op when the table
            // already matches the embedded seed data, a full reseed when the CSV/entries change.
            // Safe to run unconditionally on every startup, unlike the dev-only data seeding below.
            await SeedCountries.ExecuteSeed(scope.ServiceProvider.GetRequiredService<IPortalDbContext>());

            await scope.ServiceProvider.GetRequiredService<DefaultPageNodeSeeder>().SeedAsync();

            // Content the service ships with: the in-app guide "How to use the CMS", and the pages
            // the automated browser tests navigate to. Each file listed in
            // Data/Import/manifest.json is imported, in order, into the environments its entry
            // names. It follows the roots because shipped pages hang off them.
            await scope.ServiceProvider.GetRequiredService<ManifestContentImporter>().RunAsync(
                ManifestContentImporter.FolderIn(app.Environment.ContentRootPath), app.Environment.EnvironmentName);
            await scope.ServiceProvider
                .GetRequiredService<DfE.CheckPerformanceData.Application.Admin.DefaultAdminAccessSeeder>()
                .SeedIfEmptyAsync();
        }

        // REVIEWED BEHAVIOURAL NOTE (spec): dev seeding moves here from its old position
        // between UseHsts and UseHttpsRedirection. Equivalent because Use...() calls only
        // register handlers — nothing serves requests until app.Run().
        var seedData = app.Environment.IsDevelopment()
            || app.Configuration["SeedDevelopmentData"] == "true";
        if (seedData)
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDevDataSeedingOrchestrator>().RunAsync();
        }
    }
}
