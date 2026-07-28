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

		Assert.Equal(29, entries.Count);

		var titles = entries.Select(e => e.Title).ToList();
		Assert.DoesNotContain("Version retention", titles);
		Assert.Contains("Content staging import/export", titles);
		Assert.Contains("Pages", titles);
		Assert.Contains("Content blocks", titles);
		Assert.DoesNotContain("Visual regression dashboard", titles);
		Assert.Contains("Rules Engine", titles);
		Assert.Contains("Queues", titles);
		Assert.Contains("Rules Engine Queue", titles);
		Assert.Contains("Zendesk Queue", titles);
		Assert.Contains("Dead Letter Queue", titles);
		Assert.Contains("Rules Engine configuration", titles);
		Assert.Contains("CMS administration", titles);
		Assert.Contains("System administration", titles);
		Assert.Contains("Messages", titles);
		Assert.Contains("Deleted pages", titles);
		Assert.Contains("Seed sample pages", titles);
		Assert.Contains("Search analytics", titles);
		Assert.Contains("Search feedback", titles);
		Assert.DoesNotContain("Search messages inbox", titles);
		Assert.Contains("System settings", titles);
		Assert.DoesNotContain("CMS settings", titles);
		Assert.Contains("Storage administration", titles);
		Assert.Contains("Blob storage browser", titles);
		Assert.DoesNotContain("Debug Pipelines", titles);
		Assert.Contains("Pipeline dashboard", titles);
		Assert.Contains("Transactions", titles);
		Assert.Contains("Replay", titles);
		Assert.Contains("Amendment requests", titles);
		Assert.Contains("Uncommitted requests", titles);
		Assert.Contains("View logs", titles);
		Assert.Contains("Window administration", titles);
		Assert.Contains("Create new window", titles);
		Assert.Contains("Manage windows", titles);
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

		// CMS admin loses the messages-inbox tile (moved to the new Messages group), leaving
		// six children.
		Assert.Equal(new[] { 10, 20, 30, 40, 50, 60 }, cmsOrders);
		// System administration has four direct children: the Rules Engine sub-group (10),
		// System settings (20), Application logs (25), Role settings (30). The pipeline tiles
		// nest one level deeper under Rules Engine.
		Assert.Equal(new[] { 10, 20, 25, 30 }, systemOrders);

		var rulesEngineGroupOrders = entries
			.Where(e => e.ParentKey == "rules-engine-group")
			.Select(e => e.Order)
			.OrderBy(o => o)
			.ToArray();
		Assert.Equal(new[] { 10, 20, 30 }, rulesEngineGroupOrders);

		// Rules Engine > Queues now has two children: the RulesEngine queue (10) and the
		// Zendesk queue (20). The dead-letter queue tile moved up to the Messages group.
		var queueOrders = entries
			.Where(e => e.ParentKey == "rules-engine")
			.Select(e => e.Order)
			.OrderBy(o => o)
			.ToArray();
		Assert.Equal(new[] { 10, 20 }, queueOrders);

		// Messages group has exactly two children: Search feedback (10) and the dead-letter
		// queue (20). Same ordering the admin nav service registers them in.
		var messagesOrders = entries
			.Where(e => e.ParentKey == "messages-group")
			.Select(e => e.Order)
			.OrderBy(o => o)
			.ToArray();
		Assert.Equal(new[] { 10, 20 }, messagesOrders);
	}

	// --- DeletedPages_Tile_Links_To_Admin_Route ---

	[Fact]
	public void DeletedPages_Tile_Links_To_Admin_Route()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entry = provider.GetServices<IAdminNavEntry>()
			.Single(e => e.Key == "deleted-pages");

		Assert.Equal("/admin/pages/deleted", entry.Url);
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

	// --- DeadLetterQueue_Tile_Is_Enabled_MessagesGroup_Child_LinkingToDlq ---

	[Fact]
	public void DeadLetterQueue_Tile_Is_Enabled_MessagesGroup_Child_LinkingToDlq()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entry = provider.GetServices<IAdminNavEntry>()
			.Single(e => e.Key == "dead-letter-queue");

		Assert.Equal("messages-group", entry.ParentKey);
		Assert.Equal("/admin/queues/dlq", entry.Url);
		Assert.Equal("Dead Letter Queue", entry.Title);
		Assert.Equal("GET", entry.HttpMethod);
		Assert.Equal(20, entry.Order);
		Assert.True(entry.Enabled);
	}

	// --- MessagesInbox_Tile_Is_Enabled_MessagesGroup_Child_TitledSearchFeedback ---

	[Fact]
	public void MessagesInbox_Tile_Is_Enabled_MessagesGroup_Child_TitledSearchFeedback()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entry = provider.GetServices<IAdminNavEntry>()
			.Single(e => e.Key == "messages-inbox");

		Assert.Equal("messages-group", entry.ParentKey);
		Assert.Equal("/admin/Messages/Inbox", entry.Url);
		Assert.Equal("Search feedback", entry.Title);
		Assert.Equal("GET", entry.HttpMethod);
		Assert.Equal(10, entry.Order);
		Assert.True(entry.Enabled);
	}

	// --- MessagesGroup_Is_Root_Level_Group_Titled_Messages ---

	[Fact]
	public void MessagesGroup_Is_Root_Level_Group_Titled_Messages()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entry = provider.GetServices<IAdminNavEntry>()
			.Single(e => e.Key == "messages-group");

		Assert.Null(entry.ParentKey);
		Assert.Equal("Messages", entry.Title);
		Assert.Equal("/admin/Messages", entry.Url);
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
