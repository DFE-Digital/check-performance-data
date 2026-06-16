using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

public sealed class AdminNavRegistryTests
{
    // --- AddAdminNavEntries_Registers_Hierarchical_Entries ---

    [Fact]
	public void AddAdminNavEntries_Registers_Hierarchical_Entries()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entries = provider.GetServices<IAdminNavEntry>().ToList();

		Assert.Equal(19, entries.Count);

		var titles = entries.Select(e => e.Title).ToList();
		Assert.Contains("Version retention", titles);
		Assert.Contains("Content staging import/export", titles);
		Assert.DoesNotContain("Visual regression dashboard", titles);
		Assert.Contains("Rules Engine", titles);
		Assert.Contains("Queues", titles);
		Assert.Contains("Rules Engine Queue", titles);
		Assert.Contains("Zendesk Queue", titles);
		Assert.Contains("Dead Letter Queue", titles);
		Assert.Contains("Rules Engine configuration", titles);
		Assert.Contains("CMS administration", titles);
		Assert.Contains("System administration", titles);
		Assert.Contains("Deleted pages", titles);
		Assert.Contains("Seed sample pages", titles);
		Assert.Contains("System settings", titles);
		Assert.DoesNotContain("CMS settings", titles);
		Assert.Contains("Storage administration", titles);
		Assert.Contains("Blob storage browser", titles);
		Assert.Contains("Debug Pipelines", titles);
		Assert.Contains("Pipeline dashboard", titles);
		Assert.Contains("Transactions", titles);
		Assert.Contains("Replay", titles);
	}

	// --- Tiles_Within_Each_Group_Have_Distinct_Orders_Per_UI_Spec ---

	[Fact]
	public void Tiles_Within_Each_Group_Have_Distinct_Orders_Per_UI_Spec()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entries = provider.GetServices<IAdminNavEntry>().ToList();

		var cmsOrders = entries
			.Where(e => e.ParentKey == "cms-admin")
			.Select(e => e.Order)
			.OrderBy(o => o)
			.ToArray();

		var systemOrders = entries
			.Where(e => e.ParentKey == "system-admin")
			.Select(e => e.Order)
			.OrderBy(o => o)
			.ToArray();

		Assert.Equal(new[] { 10, 20, 30, 40 }, cmsOrders);
		// System administration now has two direct children: the Rules Engine sub-group (10)
		// and System settings (20). The pipeline tiles nest one level deeper under Rules Engine.
		Assert.Equal(new[] { 10, 20 }, systemOrders);

		var rulesEngineGroupOrders = entries
			.Where(e => e.ParentKey == "rules-engine-group")
			.Select(e => e.Order)
			.OrderBy(o => o)
			.ToArray();
		Assert.Equal(new[] { 10, 20, 30 }, rulesEngineGroupOrders);

		var queueOrders = entries
			.Where(e => e.ParentKey == "rules-engine")
			.Select(e => e.Order)
			.OrderBy(o => o)
			.ToArray();
		Assert.Equal(new[] { 10, 20, 30 }, queueOrders);
	}

	// --- DeletedPages_Tile_Has_Help_Deleted_Url ---

	[Fact]
	public void DeletedPages_Tile_Has_Help_Deleted_Url()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entry = provider.GetServices<IAdminNavEntry>()
			.Single(e => e.Key == "deleted-pages");

		Assert.Equal("/help/deleted", entry.Url);
		Assert.True(entry.Enabled);
	}

	// --- SeedSamplePages_Tile_Has_PostHttpMethod ---

	[Fact]
	public void SeedSamplePages_Tile_Has_PostHttpMethod()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entry = provider.GetServices<IAdminNavEntry>()
			.Single(e => e.Key == "seed-sample-pages");

		Assert.Equal("POST", entry.HttpMethod);
		Assert.True(entry.Enabled);
	}

	// --- SystemSettings_Tile_Is_Enabled_SystemAdmin_Child_LinkingToAdminSettings ---

	[Fact]
	public void SystemSettings_Tile_Is_Enabled_SystemAdmin_Child_LinkingToAdminSettings()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entry = provider.GetServices<IAdminNavEntry>()
			.Single(e => e.Key == "system-settings");

		Assert.Equal("system-admin", entry.ParentKey);
		Assert.Equal("/admin/settings", entry.Url);
		Assert.Equal("System settings", entry.Title);
		Assert.Equal("GET", entry.HttpMethod);
		Assert.True(entry.Enabled);
	}

	// --- DeadLetterQueue_Tile_Is_Enabled_SystemAdmin_Child_LinkingToDlq ---

	[Fact]
	public void DeadLetterQueue_Tile_Is_Enabled_SystemAdmin_Child_LinkingToDlq()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entry = provider.GetServices<IAdminNavEntry>()
			.Single(e => e.Key == "dead-letter-queue");

		Assert.Equal("rules-engine", entry.ParentKey);
		Assert.Equal("/admin/queues/dlq", entry.Url);
		Assert.Equal("Dead Letter Queue", entry.Title);
		Assert.Equal("GET", entry.HttpMethod);
		Assert.Equal(30, entry.Order);
		Assert.True(entry.Enabled);
	}

	// --- RulesConfig_Tile_Is_Enabled_SystemAdmin_Child_LinkingToAdminRules ---

	[Fact]
	public void RulesConfig_Tile_Is_Enabled_SystemAdmin_Child_LinkingToAdminRules()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entry = provider.GetServices<IAdminNavEntry>()
			.Single(e => e.Key == "rules-config");

		Assert.Equal("rules-engine-group", entry.ParentKey);
		Assert.Equal("/admin/rules", entry.Url);
		Assert.Equal("GET", entry.HttpMethod);
		Assert.Equal(30, entry.Order);
		Assert.True(entry.Enabled);
	}
}
