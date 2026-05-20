using System.Security.Claims;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

	private static DevImpersonationController CreateControllerSut(string environmentName)
	{
		var env = Substitute.For<IHostEnvironment>();
		env.EnvironmentName.Returns(environmentName);
		var http = new DefaultHttpContext();
		return new DevImpersonationController(env)
		{
			ControllerContext = new ControllerContext { HttpContext = http }
		};
	}

	// --- TicketBuilder_Admin_Cookie_Maps_To_Principal_With_AdminRole_Only ---

	[Fact]
	public void TicketBuilder_Admin_Cookie_Maps_To_Principal_With_AdminRole_Only()
	{
		var ticket = DevImpersonationTicketBuilder.TryBuild(DevImpersonationConstants.AdminValue);

		Assert.NotNull(ticket);
		Assert.True(ticket!.Principal.Identity?.IsAuthenticated);
		Assert.True(ticket.Principal.IsInRole(WikiConstants.AdminRole));
		Assert.False(ticket.Principal.IsInRole(WikiConstants.EditorRole));
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

	// --- ClaimsTransformer_Admin_Cookie_Stamps_AdminRole_Without_EditorRole ---

	[Fact]
	public async Task ClaimsTransformer_Admin_Cookie_Stamps_AdminRole_Without_EditorRole()
	{
		SetCookie(DevImpersonationConstants.AdminValue);
		var principal = new ClaimsPrincipal(new ClaimsIdentity(
			[new Claim(ClaimTypes.NameIdentifier, "user-1")],
			authenticationType: "TestScheme"));

		var result = await CreateTransformerSut().TransformAsync(principal);

		Assert.True(result.IsInRole(WikiConstants.AdminRole));
		Assert.False(result.IsInRole(WikiConstants.EditorRole));
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
}
