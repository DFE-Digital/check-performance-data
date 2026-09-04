using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// The Danger zone group and the Blob storage browser under it are registered in every
// environment. Only the Reset seed data tile is gated, by includeResetSeedData (Program.cs
// passes !IsProduction()), so the wipe-and-reseed action never surfaces in Production while
// the group still renders there around the browser.
public sealed class DangerZoneNavRegistryTests
{
    [Fact]
    public void AddAdminNavEntries_WithResetSeedData_RegistersGroupAndBothTiles()
    {
        var services = new ServiceCollection();
        services.AddAdminNavEntries(includeResetSeedData: true);

        using var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<IAdminNavEntry>().ToList();

        var group = entries.Single(e => e.Key == "danger-zone");
        Assert.Null(group.ParentKey);
        Assert.Equal("Danger zone", group.Title);
        Assert.True(group.Enabled);

        var tile = entries.Single(e => e.Key == "reset-seed-data");
        Assert.Equal("danger-zone", tile.ParentKey);
        Assert.Equal("/admin/danger-zone/reset-seed-data", tile.Url);
        Assert.Equal("GET", tile.HttpMethod);
        Assert.True(tile.Enabled);

        var browser = entries.Single(e => e.Key == "storage-browser");
        Assert.Equal("danger-zone", browser.ParentKey);
        Assert.Equal("/admin/storage", browser.Url);
        Assert.True(browser.Enabled);
    }

    [Fact]
    public void AddAdminNavEntries_WithoutResetSeedData_KeepsGroupAndBrowser_OmitsResetTile()
    {
        var services = new ServiceCollection();
        services.AddAdminNavEntries(includeResetSeedData: false);

        using var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<IAdminNavEntry>().ToList();

        Assert.DoesNotContain(entries, e => e.Key == "reset-seed-data");

        // The group and the browser survive: on Production the group holds the browser alone.
        Assert.Contains(entries, e => e.Key == "danger-zone");
        Assert.Contains(entries, e => e.Key == "storage-browser");
    }

    [Fact]
    public void AddAdminNavEntries_Default_OmitsResetSeedDataOnly()
    {
        var services = new ServiceCollection();
        services.AddAdminNavEntries();

        using var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<IAdminNavEntry>().ToList();

        Assert.DoesNotContain(entries, e => e.Key == "reset-seed-data");
        Assert.Contains(entries, e => e.Key == "danger-zone");
    }
}
