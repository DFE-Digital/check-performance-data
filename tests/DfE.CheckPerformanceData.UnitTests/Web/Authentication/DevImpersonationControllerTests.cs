using DfE.CheckPerformanceData.Web.Authentication;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Authentication;

public sealed class DevImpersonationControllerTests
{
	private static DevImpersonationController CreateSut(string environmentName, string? referrer = null)
	{
		var env = Substitute.For<IHostEnvironment>();
		env.EnvironmentName.Returns(environmentName);

		var http = new DefaultHttpContext();
		if (referrer is not null)
		{
			http.Request.Headers["Referer"] = referrer;
		}

		return new DevImpersonationController(env)
		{
			ControllerContext = new ControllerContext { HttpContext = http }
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

	// --- Non-prod deployed environments (QA, preprod) are allowed ---

	[Theory]
	[InlineData("Development")]
	[InlineData("Staging")]
	[InlineData("QA")]
	[InlineData("Preproduction")]
	public void Editor_IsAllowed_InAnyNonProductionEnvironment(string environmentName)
	{
		var sut = CreateSut(environmentName);

		var result = sut.Editor();

		Assert.IsType<RedirectResult>(result);
		Assert.NotNull(GetSetCookieHeader(sut));
	}
}
