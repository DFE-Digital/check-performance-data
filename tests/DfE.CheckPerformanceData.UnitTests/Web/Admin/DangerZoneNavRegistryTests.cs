using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// The Danger zone group + Reset seed data tile are registered only when includeDangerZone
// is true (Program.cs passes !IsProduction()), so the destructive reset never surfaces in
// Production. The default overload (includeDangerZone: false) leaves them out entirely.
public sealed class DangerZoneNavRegistryTests
{
    [Fact]
    public void AddAdminNavEntries_WithDangerZone_RegistersGroupAndTile()
    {
        var services = new ServiceCollection();
        services.AddAdminNavEntries(includeDangerZone: true);

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
    }

    [Fact]
    public void AddAdminNavEntries_WithoutDangerZone_OmitsGroupAndTile()
    {
        var services = new ServiceCollection();
        services.AddAdminNavEntries(includeDangerZone: false);

        using var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<IAdminNavEntry>().ToList();

        Assert.DoesNotContain(entries, e => e.Key == "danger-zone");
        Assert.DoesNotContain(entries, e => e.Key == "reset-seed-data");
    }

    [Fact]
    public void AddAdminNavEntries_Default_OmitsDangerZone()
    {
        var services = new ServiceCollection();
        services.AddAdminNavEntries();

        using var provider = services.BuildServiceProvider();
        var entries = provider.GetServices<IAdminNavEntry>().ToList();

        Assert.DoesNotContain(entries, e => e.Key == "danger-zone");
    }
}
