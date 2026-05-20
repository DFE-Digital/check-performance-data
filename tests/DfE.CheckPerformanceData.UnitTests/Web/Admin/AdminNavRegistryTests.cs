using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

public sealed class AdminNavRegistryTests
{
	// --- AddAdminNavEntries_Registers_Three_PreSeeded_Entries_All_Disabled ---

	[Fact]
	public void AddAdminNavEntries_Registers_Three_PreSeeded_Entries_All_Disabled()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var entries = provider.GetServices<IAdminNavEntry>().ToList();

		Assert.Equal(3, entries.Count);
		Assert.All(entries, e => Assert.False(e.Enabled));
		Assert.All(entries, e => Assert.Equal(string.Empty, e.Url));

		var titles = entries.Select(e => e.Title).ToList();
		Assert.Contains(titles, t => t.Contains("Version retention", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(titles, t => t.Contains("Visual Regression Dashboard", StringComparison.OrdinalIgnoreCase));
		Assert.Contains(titles, t => t.Contains("Content Staging Import", StringComparison.OrdinalIgnoreCase));
	}

	// --- Registered_Entries_Have_Orders_10_20_30 ---

	[Fact]
	public void Registered_Entries_Have_Orders_10_20_30()
	{
		var services = new ServiceCollection();
		services.AddAdminNavEntries();

		using var provider = services.BuildServiceProvider();
		var orders = provider.GetServices<IAdminNavEntry>()
			.Select(e => e.Order)
			.OrderBy(o => o)
			.ToArray();

		Assert.Equal(new[] { 10, 20, 30 }, orders);
	}
}
