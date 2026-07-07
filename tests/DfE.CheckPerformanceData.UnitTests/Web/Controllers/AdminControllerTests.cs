using System.Reflection;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
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

	// --- Controller_Gated_By_AllowAnyGrantedSection ---

	[Fact]
	public void Controller_Gated_By_AllowAnyGrantedSection()
	{
		// The landing is not tied to a specific section grant — anyone with at least one
		// grant reaches it, and the sidebar then hides sections their role does not have.
		// The gate lives at the class level so every future action on the controller inherits it.
		var gate = typeof(AdminController).GetCustomAttribute<RequireAdminSectionAttribute>();
		Assert.NotNull(gate);
		Assert.True(gate!.AllowAnyGrantedSection);
		Assert.Null(gate.SectionKey);
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

	// --- Index_Returns_AdminLandingViewModel_As_Forest_Sorted_By_Order ---

	[Fact]
	public void Index_Returns_AdminLandingViewModel_As_Forest_Sorted_By_Order()
	{
		// Two groups, two children each. Children intentionally out of Order to verify
		// per-group sorting. Groups intentionally out of Order in the list to verify
		// top-level sorting. The landing page now uses the recursive forest model.
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

		Assert.Equal(2, model.Roots.Count);
		Assert.Equal("cms-admin", model.Roots[0].Entry.Key);
		Assert.Equal("system-admin", model.Roots[1].Entry.Key);

		Assert.Collection(
			model.Roots[0].Children,
			c => Assert.Equal(10, c.Entry.Order),
			c => Assert.Equal(20, c.Entry.Order));

		Assert.Collection(
			model.Roots[1].Children,
			c => Assert.Equal(10, c.Entry.Order),
			c => Assert.Equal(20, c.Entry.Order));
	}

	// --- Index_Nests_Empty_Url_Container_With_Its_Child_Links ---

	[Fact]
	public void Index_Nests_Empty_Url_Container_With_Its_Child_Links()
	{
		// Regression: the "Rules Engine" sub-group is a container with an empty Url whose children
		// are the real pages. The landing model must expose the container WITH its children nested,
		// so the view can render the children as working links instead of rendering the container
		// itself as a dead <a href="">.
		var group = StubEntry(key: "system-admin", parentKey: null, order: 10,
			title: "System administration");
		var container = StubEntry(key: "rules-engine-group", parentKey: "system-admin", order: 10,
			title: "Rules Engine", url: "", enabled: true);
		var page = StubEntry(key: "rules-config", parentKey: "rules-engine-group", order: 30,
			title: "Rules Engine configuration", url: "/admin/rules", enabled: true);

		var sut = new AdminController(new List<IAdminNavEntry> { group, container, page });

		var result = sut.Index();

		var model = Assert.IsType<AdminLandingViewModel>(Assert.IsType<ViewResult>(result).Model);

		var systemAdmin = Assert.Single(model.Roots);
		var containerNode = Assert.Single(systemAdmin.Children);
		Assert.Equal("rules-engine-group", containerNode.Entry.Key);
		Assert.Empty(containerNode.Entry.Url);

		var pageNode = Assert.Single(containerNode.Children);
		Assert.Equal("/admin/rules", pageNode.Entry.Url);
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
