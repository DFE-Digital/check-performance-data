using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Authentication;

public sealed class DevImpersonationControllerTests
{
	// The surface is gated on Dev:ToolsEnabled AND not-Production. Tests default the flag
	// on so the cookie/redirect assertions exercise the happy path; flag-off and Production
	// cases are asserted explicitly below.
	private static DevImpersonationController CreateSut(
		string environmentName, string? referrer = null, bool devToolsEnabled = true)
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
		if (referrer is not null)
		{
			http.Request.Headers["Referer"] = referrer;
		}

		var urlHelper = Substitute.For<IUrlHelper>();
		// IUrlHelper.IsLocalUrl is a real interface member in this ASP.NET Core version (not the
		// extension method of older versions), so an unconfigured substitute answers false for
		// everything — it has to be stubbed to mirror the real "starts with a single '/'" rule.
		urlHelper.IsLocalUrl(Arg.Any<string?>()).Returns(call =>
		{
			var candidate = call.Arg<string?>();
			return !string.IsNullOrEmpty(candidate) && candidate.StartsWith('/') && !candidate.StartsWith("//");
		});

		return new DevImpersonationController(config, env)
		{
			ControllerContext = new ControllerContext { HttpContext = http },
			Url = urlHelper
		};
	}

	private static string? GetSetCookieHeader(DevImpersonationController controller) =>
		controller.Response.Headers["Set-Cookie"].FirstOrDefault();

	// --- Editor sets the cookie to "editor" and redirects ---

	[Fact]
	public void Editor_SetsCookieToEditor_AndRedirectsToReferrer()
	{
		var sut = CreateSut("Development", referrer: "/help");

		var result = sut.Editor();

		var redirect = Assert.IsType<RedirectResult>(result);
		Assert.Equal("/help", redirect.Url);

		var setCookie = GetSetCookieHeader(sut);
		Assert.NotNull(setCookie);
		Assert.Contains($"{DevImpersonationConstants.CookieName}={DevImpersonationConstants.EditorValue}", setCookie);
	}

	// --- User sets the cookie to "user" and redirects ---

	[Fact]
	public void User_SetsCookieToUser_AndRedirectsToReferrer()
	{
		var sut = CreateSut("Development", referrer: "/help/some-page");

		var result = sut.User();

		var redirect = Assert.IsType<RedirectResult>(result);
		Assert.Equal("/help/some-page", redirect.Url);

		var setCookie = GetSetCookieHeader(sut);
		Assert.NotNull(setCookie);
		Assert.Contains($"{DevImpersonationConstants.CookieName}={DevImpersonationConstants.UserValue}", setCookie);
	}

	// --- Falls back to "/" when no Referer header is present ---

	[Fact]
	public void Editor_RedirectsToRoot_WhenNoReferrerHeader()
	{
		var sut = CreateSut("Development");

		var result = sut.Editor();

		var redirect = Assert.IsType<RedirectResult>(result);
		Assert.Equal("/", redirect.Url);
	}

	// --- Cookie carries HttpOnly + SameSite=Strict ---

	[Fact]
	public void Editor_SetsCookieWithHttpOnlyAndSameSiteStrict()
	{
		var sut = CreateSut("Development");

		sut.Editor();

		var setCookie = GetSetCookieHeader(sut)!;
		Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
	}

	// --- Production environment returns 404 (never enabled in prod) ---

	[Fact]
	public void Editor_Returns404_InProductionEnvironment()
	{
		var sut = CreateSut(Environments.Production);

		var result = sut.Editor();

		Assert.IsType<NotFoundResult>(result);
		Assert.Null(GetSetCookieHeader(sut));
	}

	[Fact]
	public void User_Returns404_InProductionEnvironment()
	{
		var sut = CreateSut(Environments.Production);

		var result = sut.User();

		Assert.IsType<NotFoundResult>(result);
		Assert.Null(GetSetCookieHeader(sut));
	}

	// --- Allowed in non-production environments only when Dev:ToolsEnabled is set ---
	// (local dev + ephemeral PR/review apps set the flag; deployed DEV/QA/Preproduction
	// do not, so the surface is hidden there).

	[Theory]
	[InlineData("Development")]
	[InlineData("Review")]
	public void Editor_IsAllowed_WhenDevToolsEnabled_AndNotProduction(string environmentName)
	{
		var sut = CreateSut(environmentName, devToolsEnabled: true);

		var result = sut.Editor();

		Assert.IsType<RedirectResult>(result);
		Assert.NotNull(GetSetCookieHeader(sut));
	}

	// --- 404 in non-production environments that do NOT enable the dev tooling surface ---

	[Theory]
	[InlineData("Development")]
	[InlineData("QA")]
	[InlineData("Preproduction")]
	[InlineData("Staging")]
	public void Editor_Returns404_WhenDevToolsDisabled(string environmentName)
	{
		var sut = CreateSut(environmentName, devToolsEnabled: false);

		var result = sut.Editor();

		Assert.IsType<NotFoundResult>(result);
		Assert.Null(GetSetCookieHeader(sut));
	}

	// --- Production 404s even if the flag is left on (hard guard on top of the flag) ---

	[Fact]
	public void Editor_Returns404_InProduction_EvenWhenDevToolsEnabled()
	{
		var sut = CreateSut(Environments.Production, devToolsEnabled: true);

		var result = sut.Editor();

		Assert.IsType<NotFoundResult>(result);
		Assert.Null(GetSetCookieHeader(sut));
	}

	// --- Clear deletes the cookie (distinct from User which keeps a synthetic principal) ---

	[Fact]
	public void Clear_DeletesCookie_AndRedirectsToReferrer()
	{
		var sut = CreateSut("Development", referrer: "/help");

		var result = sut.Clear();

		var redirect = Assert.IsType<RedirectResult>(result);
		Assert.Equal("/help", redirect.Url);

		// Response.Cookies.Delete emits a Set-Cookie with an expiry in the distant past.
		var setCookie = GetSetCookieHeader(sut);
		Assert.NotNull(setCookie);
		Assert.Contains(DevImpersonationConstants.CookieName, setCookie);
		Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Clear_RedirectsToRoot_WhenNoReferrerHeader()
	{
		var sut = CreateSut("Development");

		var result = sut.Clear();

		var redirect = Assert.IsType<RedirectResult>(result);
		Assert.Equal("/", redirect.Url);
	}

	[Fact]
	public void Clear_Returns404_InProductionEnvironment()
	{
		var sut = CreateSut(Environments.Production);

		var result = sut.Clear();

		Assert.IsType<NotFoundResult>(result);
		Assert.Null(GetSetCookieHeader(sut));
	}

	[Theory]
	[InlineData("Development")]
	[InlineData("Review")]
	public void Clear_IsAllowed_WhenDevToolsEnabled_AndNotProduction(string environmentName)
	{
		var sut = CreateSut(environmentName, devToolsEnabled: true);

		var result = sut.Clear();

		Assert.IsType<RedirectResult>(result);
		Assert.NotNull(GetSetCookieHeader(sut));
	}

	[Fact]
	public void Clear_Returns404_WhenDevToolsDisabled()
	{
		var sut = CreateSut("QA", devToolsEnabled: false);

		var result = sut.Clear();

		Assert.IsType<NotFoundResult>(result);
		Assert.Null(GetSetCookieHeader(sut));
	}

	// --- AB#298317: an explicit returnUrl overrides the Referer-based redirect ---
	// (Check your pupil data's "No, I'd like to sign out" answer needs this: its own Referer is an
	// authenticated-only page that would otherwise bounce the now-signed-out browser into a fresh
	// sign-in challenge instead of showing it has signed out.)

	[Fact]
	public void Clear_WithReturnUrl_RedirectsThereInsteadOfTheReferrer()
	{
		var sut = CreateSut("Development", referrer: "/CheckYourPupilData/some-window");

		var result = sut.Clear(returnUrl: "/");

		var redirect = Assert.IsType<RedirectResult>(result);
		Assert.Equal("/", redirect.Url);
	}

	[Fact]
	public void Clear_WithAnExternalReturnUrl_FallsBackToTheReferrer()
	{
		// Untrusted query-string input: never redirect the browser off-site.
		var sut = CreateSut("Development", referrer: "/help");

		var result = sut.Clear(returnUrl: "https://evil.example.com/");

		var redirect = Assert.IsType<RedirectResult>(result);
		Assert.Equal("/help", redirect.Url);
	}
}
