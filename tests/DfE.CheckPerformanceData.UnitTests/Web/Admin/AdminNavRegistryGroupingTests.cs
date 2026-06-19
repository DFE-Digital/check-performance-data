using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// Hierarchy-shape assertions: defends against future tenant phases adding tiles whose
// ParentKey does not match an existing group, or registrations that collide on Key.
public sealed class AdminNavRegistryGroupingTests
{
	private static IReadOnlyList<IAdminNavEntry> ResolveEntries()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();
		var provider = services.BuildServiceProvider();
		return provider.GetServices<IAdminNavEntry>().ToList();
	}

	// --- Groups_Have_Null_ParentKey ---

	[Fact]
	public void Groups_Have_Null_ParentKey()
	{
		var entries = ResolveEntries();

		var groups = entries.Where(e => e.ParentKey is null).ToList();

		Assert.Equal(3, groups.Count);
		var groupKeys = groups.Select(g => g.Key).ToHashSet();
		Assert.Contains("cms-admin", groupKeys);
		Assert.Contains("system-admin", groupKeys);
		Assert.Contains("storage-admin", groupKeys);
	}

	// --- Tiles_Have_NonNull_ParentKey_Matching_An_Existing_Entry ---

	[Fact]
	public void Tiles_Have_NonNull_ParentKey_Matching_An_Existing_Entry()
	{
		var entries = ResolveEntries();

		// With deep (3-4 level) nesting a ParentKey may reference either a top-level group
		// or an intermediate entry; every ParentKey must still resolve to a registered Key.
		var allKeys = entries.Select(e => e.Key).ToHashSet();
		var tiles = entries.Where(e => e.ParentKey is not null).ToList();

		Assert.NotEmpty(tiles);
		foreach (var tile in tiles)
		{
			Assert.Contains(tile.ParentKey!, allKeys);
		}
	}

    // --- All_Keys_Are_Unique_Across_Registrations ---

    [Fact]
	public void All_Keys_Are_Unique_Across_Registrations()
	{
		var entries = ResolveEntries();

		var keys = entries.Select(e => e.Key).ToList();

		Assert.Equal(18, keys.Count);
		Assert.Equal(keys.Count, keys.Distinct().Count());
	}

	// --- RulesEngine_Queues_Is_RulesEngineGroup_Child_Order_20_Live ---

	[Fact]
	public void RulesEngine_Queues_Is_RulesEngineGroup_Child_Order_20_Live()
	{
		var entries = ResolveEntries();

		var rulesEngine = entries.Single(e => e.Key == "rules-engine");

		Assert.Equal("rules-engine-group", rulesEngine.ParentKey);
		Assert.Equal(20, rulesEngine.Order);
		Assert.True(rulesEngine.Enabled);
		Assert.Equal("/admin/queues", rulesEngine.Url);
	}

	// --- CmsAdmin_Group_Has_Order_10_And_SystemAdmin_Has_Order_20 ---

	[Fact]
	public void CmsAdmin_Group_Has_Order_10_And_SystemAdmin_Has_Order_20()
	{
		var entries = ResolveEntries();

		var cms = entries.Single(e => e.Key == "cms-admin");
		var system = entries.Single(e => e.Key == "system-admin");

		Assert.Null(cms.ParentKey);
		Assert.Equal(10, cms.Order);
		Assert.Null(system.ParentKey);
		Assert.Equal(20, system.Order);
	}
}
