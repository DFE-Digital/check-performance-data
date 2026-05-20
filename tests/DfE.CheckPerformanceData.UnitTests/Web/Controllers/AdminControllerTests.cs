using System.Reflection;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers;
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

	// --- Index_Returns_ViewResult_With_NavEntries_Sorted_By_Order ---

	[Fact]
	public void Index_Returns_ViewResult_With_NavEntries_Sorted_By_Order()
	{
		var thirty = StubEntry(order: 30, title: "Third");
		var ten = StubEntry(order: 10, title: "First");
		var twenty = StubEntry(order: 20, title: "Second");

		var entries = new List<IAdminNavEntry> { thirty, ten, twenty };
		var sut = new AdminController(entries);

		var result = sut.Index();

		var view = Assert.IsType<ViewResult>(result);
		var model = Assert.IsAssignableFrom<IReadOnlyList<IAdminNavEntry>>(view.Model);
		Assert.Collection(
			model,
			e => Assert.Equal(10, e.Order),
			e => Assert.Equal(20, e.Order),
			e => Assert.Equal(30, e.Order));
	}

	private static IAdminNavEntry StubEntry(int order, string title)
	{
		var entry = Substitute.For<IAdminNavEntry>();
		entry.Order.Returns(order);
		entry.Title.Returns(title);
		entry.Description.Returns($"{title} description");
		entry.Url.Returns(string.Empty);
		entry.Enabled.Returns(false);
		return entry;
	}
}
