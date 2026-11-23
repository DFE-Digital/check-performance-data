using System.Security.Claims;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Authentication;

// The admin session sign-in uses the real cookie scheme, so that tests get a session for which the
// establishment context guard is live. It must stay as closed as the other dev sign-ins.
public sealed class DevImpersonationAdminSessionTests
{
	private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();

	private DevImpersonationController CreateSut(string environmentName, bool devToolsEnabled = true)
	{
		var env = Substitute.For<IHostEnvironment>();
		env.EnvironmentName.Returns(environmentName);
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[SettingKeys.DevToolsEnabled] = devToolsEnabled ? "true" : "false"
			})
			.Build();
		var http = new DefaultHttpContext
		{
			RequestServices = new ServiceCollection().AddSingleton(_authentication).BuildServiceProvider()
		};
		return new DevImpersonationController(config, env)
		{
			ControllerContext = new ControllerContext { HttpContext = http }
		};
	}

	[Fact]
	public async Task AdminSession_SignsInAnAdminThroughTheCookieScheme()
	{
		var sut = CreateSut(Environments.Development);

		var result = await sut.AdminSession();

		Assert.IsType<RedirectResult>(result);
		await _authentication.Received(1).SignInAsync(
			Arg.Any<HttpContext>(),
			CookieAuthenticationDefaults.AuthenticationScheme,
			Arg.Is<ClaimsPrincipal>(p => p.IsInRole(WikiConstants.AdminRole) && p.IsInRole(WikiConstants.EditorRole)),
			Arg.Any<AuthenticationProperties?>());
	}

	[Fact]
	public async Task AdminSession_RemovesAnyDevImpersonationCookie()
	{
		var sut = CreateSut(Environments.Development);

		await sut.AdminSession();

		var setCookie = sut.Response.Headers.SetCookie.ToString();
		Assert.Contains(DevImpersonationConstants.CookieName + "=;", setCookie);
	}

	[Theory]
	[InlineData("Production", true)]
	[InlineData("Development", false)]
	public async Task AdminSession_IsNotFound_WhenDevToolsAreOff(string environmentName, bool devToolsEnabled)
	{
		var sut = CreateSut(environmentName, devToolsEnabled);

		var result = await sut.AdminSession();

		Assert.IsType<NotFoundResult>(result);
		await _authentication.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
	}
}
