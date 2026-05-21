using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

public sealed class AdminNavRegistryTests
{
	// --- AddAdminNavEntries_Registers_Eight_Hierarchical_Entries ---

	[Fact]
	public void AddAdminNavEntries_Registers_Eight_Hierarchical_Entries()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entries = provider.GetServices<IAdminNavEntry>().ToList();

		Assert.Equal(8, entries.Count);

		var titles = entries.Select(e => e.Title).ToList();
		Assert.Contains("Version retention", titles);
		Assert.Contains("Content staging import/export", titles);
		Assert.Contains("Visual regression dashboard", titles);
		Assert.Contains("Rules engine", titles);
		Assert.Contains("CMS administration", titles);
		Assert.Contains("System administration", titles);
		Assert.Contains("Deleted pages", titles);
		Assert.Contains("Seed sample pages", titles);
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
		Assert.Equal(new[] { 10, 20 }, systemOrders);
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
}
