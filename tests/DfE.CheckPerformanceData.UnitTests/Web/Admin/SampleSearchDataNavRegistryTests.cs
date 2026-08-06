using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// TestDataController serves the seed-sample-search-data page only in Development, Review
// and QA; everywhere else it returns NotFound. The tile that links to it was registered
// unconditionally and its section grant is topped up to admin on every startup, so a
// Preproduction or Production admin saw a "Seed sample search data" tile in the sidebar
// that dead-ended in a 404. Registration now follows the same environment whitelist as
// the controller — the same shape the Danger zone group already uses.
public sealed class SampleSearchDataNavRegistryTests
{
    [Fact]
    public void AddAdminNavEntries_WithSampleSearchData_RegistersTheTile()
    {
        var services = new ServiceCollection();
        services.AddAdminNavEntries(includeSampleSearchData: true);

        using var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<IAdminNavEntry>().ToList();

        var tile = entries.Single(e => e.Key == AdminNavKeys.SeedSampleSearchData);
        Assert.Equal(AdminNavKeys.TestDataGroup, tile.ParentKey);
        Assert.Equal("/admin/test-data/sample-search-data", tile.Url);
        Assert.True(tile.Enabled);
    }

    [Fact]
    public void AddAdminNavEntries_WithoutSampleSearchData_OmitsTheTile()
    {
        var services = new ServiceCollection();
        services.AddAdminNavEntries(includeSampleSearchData: false);

        using var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<IAdminNavEntry>().ToList();

        Assert.DoesNotContain(entries, e => e.Key == AdminNavKeys.SeedSampleSearchData);

        // The group survives — it still owns the Seed sample CMS pages tile, which has no
        // environment gate of its own.
        Assert.Contains(entries, e => e.Key == AdminNavKeys.TestDataGroup);
        Assert.Contains(entries, e => e.Key == AdminNavKeys.SeedSamplePages);
    }

    [Fact]
    public void AddAdminNavEntries_Default_OmitsTheTile()
    {
        // Defaulting to "off" keeps the safe behaviour when a caller forgets the flag —
        // a missing tile is recoverable, a 404 dead-end in Production is not.
        var services = new ServiceCollection();
        services.AddAdminNavEntries();

        using var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<IAdminNavEntry>().ToList();

        Assert.DoesNotContain(entries, e => e.Key == AdminNavKeys.SeedSampleSearchData);
    }
}
