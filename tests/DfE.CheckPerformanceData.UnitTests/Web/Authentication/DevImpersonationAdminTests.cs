using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Authentication;

public sealed class DevImpersonationAdminTests
{
	private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

	private DevImpersonationClaimsTransformer CreateTransformerSut() => new(_httpContextAccessor);

	private void SetCookie(string? value)
	{
		var context = new DefaultHttpContext();
		if (value is not null)
		{
			context.Request.Headers["Cookie"] =
				$"{DevImpersonationConstants.CookieName}={value}";
		}
		_httpContextAccessor.HttpContext.Returns(context);
	}

	private static DevImpersonationController CreateControllerSut(
		string environmentName, bool devToolsEnabled = true)
	{
		var env = Substitute.For<IHostEnvironment>();
		env.EnvironmentName.Returns(environmentName);
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[SettingKeys.DevToolsEnabled] = devToolsEnabled ? "true" : "false"
			})
			.Build();
		var http = new DefaultHttpContext();
		return new DevImpersonationController(config, env)
		{
			ControllerContext = new ControllerContext { HttpContext = http }
		};
	}

	// --- TicketBuilder_Admin_Cookie_Stamps_AdminAndEditorRoles ---

	[Fact]
	public void TicketBuilder_Admin_Cookie_Stamps_AdminAndEditorRoles()
	{
		// Admin implies editor (one-way hierarchy) — the admin cookie stamps both role
		// claims so every existing [Authorize(Roles = EditorRole)] endpoint accepts an
		// admin principal without per-endpoint composition. Editor cookie still does NOT
		// grant admin (asserted by the absence of a corresponding test in this class).
		var ticket = DevImpersonationTicketBuilder.TryBuild(DevImpersonationConstants.AdminValue);

		Assert.NotNull(ticket);
		Assert.True(ticket!.Principal.Identity?.IsAuthenticated);
		Assert.True(ticket.Principal.IsInRole(WikiConstants.AdminRole));
		Assert.True(ticket.Principal.IsInRole(WikiConstants.EditorRole));
	}

	// --- TicketBuilder_Editor_Cookie_Does_Not_Grant_Admin ---

	[Fact]
	public void TicketBuilder_Editor_Cookie_Does_Not_Grant_Admin()
	{
		// Hierarchy is one-way — editor never implies admin.
		var ticket = DevImpersonationTicketBuilder.TryBuild(DevImpersonationConstants.EditorValue);

		Assert.NotNull(ticket);
		Assert.True(ticket!.Principal.IsInRole(WikiConstants.EditorRole));
		Assert.False(ticket.Principal.IsInRole(WikiConstants.AdminRole));
	}

	// --- TicketBuilder_Unknown_Cookie_Value_Returns_Null ---

	[Fact]
	public void TicketBuilder_Unknown_Cookie_Value_Returns_Null()
	{
		// Regression — unknown values must still produce no ticket, so the Wave 1
		// extension can't accidentally widen the guard to accept arbitrary strings.
		var ticket = DevImpersonationTicketBuilder.TryBuild("not-a-role");

		Assert.Null(ticket);
	}

	// --- ClaimsTransformer_Admin_Cookie_Stamps_BothAdminAndEditorRoles ---

	[Fact]
	public async Task ClaimsTransformer_Admin_Cookie_Stamps_BothAdminAndEditorRoles()
	{
		// Admin implies editor (one-way hierarchy). The transformer overlays both role
		// claims so editor-gated endpoints accept admin principals without per-endpoint
		// policy composition.
		SetCookie(DevImpersonationConstants.AdminValue);
		var principal = new ClaimsPrincipal(new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, "user-1")],
			authenticationType: "TestScheme"));

		var result = await CreateTransformerSut().TransformAsync(principal);

		Assert.True(result.IsInRole(WikiConstants.AdminRole));
		Assert.True(result.IsInRole(WikiConstants.EditorRole));
	}

	// --- ClaimsTransformer_Admin_Cookie_Does_Not_Clobber_Existing_EditorRole ---

	[Fact]
	public async Task ClaimsTransformer_Admin_Cookie_Does_Not_Clobber_Existing_EditorRole()
	{
		// A DfE admin who is also genuinely granted the editor role at DSI level. The
		// admin cookie ADDS the admin claim — it MUST NOT remove the editor claim.
		SetCookie(DevImpersonationConstants.AdminValue);
		var principal = new ClaimsPrincipal(new ClaimsIdentity(
			[
				new Claim(ClaimTypes.NameIdentifier, "user-1"),
				new Claim(ClaimTypes.Role, WikiConstants.EditorRole)
			],
			authenticationType: "TestScheme"));

		var result = await CreateTransformerSut().TransformAsync(principal);

		Assert.True(result.IsInRole(WikiConstants.AdminRole));
		Assert.True(result.IsInRole(WikiConstants.EditorRole));
	}

	// --- DevImpersonationController_Admin_Returns_NotFound_When_IsProduction ---

	[Fact]
	public void DevImpersonationController_Admin_Returns_NotFound_When_IsProduction()
	{
		// Mirrors the existing Editor/User/Clear guards — the new admin action must
		// 404 in Production so the dev-impersonation path is impossible to invoke.
		var sut = CreateControllerSut(Environments.Production);

		var result = sut.Admin();

		Assert.IsType<NotFoundResult>(result);
	}

	// --- Admin 404s in a non-production env when the dev tooling surface is disabled ---

	[Fact]
	public void DevImpersonationController_Admin_Returns_NotFound_When_DevToolsDisabled()
	{
		// Deployed DEV/QA/Preproduction never set Dev:ToolsEnabled, so even the highest-
		// privilege impersonation action is unreachable there.
		var sut = CreateControllerSut("QA", devToolsEnabled: false);

		var result = sut.Admin();

		Assert.IsType<NotFoundResult>(result);
	}
}
