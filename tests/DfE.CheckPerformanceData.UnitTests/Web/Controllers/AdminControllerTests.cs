using System.Reflection;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public sealed class AdminControllerTests
{
	// --- AdminRole_Const_Has_Exact_Cypmd_Admin_Value ---

	[Fact]
	public void AdminRole_Const_Has_Exact_Cypmd_Admin_Value()
	{
		// Typo-failure surface: literal string assertion catches a typo in the constant.
		// Don't reference WikiConstants.AdminRole on both sides — pin the literal.
		Assert.Equal("cypmd_admin", WikiConstants.AdminRole);
	}

	// --- Index_Action_Has_Authorize_Attribute_With_AdminRole ---

	[Fact]
	public void Index_Action_Has_Authorize_Attribute_With_AdminRole()
	{
		var indexMethod = typeof(AdminController).GetMethod("Index");
		Assert.NotNull(indexMethod);

		var authorize = indexMethod!.GetCustomAttribute<AuthorizeAttribute>();
		Assert.NotNull(authorize);
		Assert.Equal("cypmd_admin", authorize!.Roles);
	}

	// --- Index_Action_Has_HttpGet_Admin_Route ---

	[Fact]
	public void Index_Action_Has_HttpGet_Admin_Route()
	{
		var indexMethod = typeof(AdminController).GetMethod("Index");
		Assert.NotNull(indexMethod);

		var httpGet = indexMethod!.GetCustomAttribute<HttpGetAttribute>();
		Assert.NotNull(httpGet);
		Assert.Equal("admin", httpGet!.Template);
	}

	// --- Index_Returns_AdminLandingViewModel_Grouped_By_ParentKey ---

	[Fact]
	public void Index_Returns_AdminLandingViewModel_Grouped_By_ParentKey()
	{
		// Two groups, two children each. Children intentionally out of Order to verify
		// per-group sorting. Groups intentionally out of Order in the list to verify
		// top-level sorting.
		var groupA = StubEntry(key: "cms-admin", parentKey: null, order: 10, title: "CMS administration");
		var groupAChildHigh = StubEntry(key: "child-a-hi", parentKey: "cms-admin", order: 20, title: "Child A2");
		var groupAChildLow = StubEntry(key: "child-a-lo", parentKey: "cms-admin", order: 10, title: "Child A1");

		var groupB = StubEntry(key: "system-admin", parentKey: null, order: 20, title: "System administration");
		var groupBChildHigh = StubEntry(key: "child-b-hi", parentKey: "system-admin", order: 20, title: "Child B2");
		var groupBChildLow = StubEntry(key: "child-b-lo", parentKey: "system-admin", order: 10, title: "Child B1");

		var entries = new List<IAdminNavEntry>
		{
			groupB, groupAChildHigh, groupA, groupBChildLow, groupAChildLow, groupBChildHigh
		};

		var sut = new AdminController(entries);

		var result = sut.Index();

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsType<AdminLandingViewModel>(view.Model);

		Assert.Equal(2, model.Groups.Count);
		Assert.Equal("cms-admin", model.Groups[0].Key);
		Assert.Equal("system-admin", model.Groups[1].Key);

		Assert.Collection(
			model.Groups[0].Children,
			c => Assert.Equal(10, c.Order),
			c => Assert.Equal(20, c.Order));

		Assert.Collection(
			model.Groups[1].Children,
			c => Assert.Equal(10, c.Order),
			c => Assert.Equal(20, c.Order));
	}

	private static IAdminNavEntry StubEntry(
		string key,
		string? parentKey,
		int order,
		string title,
		string description = "d",
		string url = "",
		bool enabled = false,
		string httpMethod = "GET")
	{
		var entry = Substitute.For<IAdminNavEntry>();
		entry.Key.Returns(key);
		entry.ParentKey.Returns(parentKey);
		entry.Order.Returns(order);
		entry.Title.Returns(title);
		entry.Description.Returns(description);
		entry.Url.Returns(url);
		entry.Enabled.Returns(enabled);
		entry.HttpMethod.Returns(httpMethod);
		return entry;
	}
}
