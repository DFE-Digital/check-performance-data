using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DfE.CheckPerformanceData.Web.Startup;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.IntegrationTests.Web;

// Flash messages must not travel through the browser.
//
// MVC's default TempData provider hands every value to the client as a cookie, and the client
// then presents it back on every subsequent request to the site. Behind the deployed ingress
// that is a hard failure mode rather than a slow one: once the request headers outgrow the
// proxy's buffer the proxy answers 400 itself, the app never sees the request, and the cookie
// is still there on the next attempt — so every page stays broken until the user clears their
// cookies. An admin action that produces a few kilobytes of banner text is enough to cross the
// line on top of the chunked authentication cookie a signed-in user already carries.
//
// These assertions are at the pipeline level deliberately: the wiring under test is a service
// registration, and only an actual request/response round trip can show whether a cookie was
// written.
public sealed class TempDataStorageTests
{
    private const string CookieTempDataName = ".AspNetCore.Mvc.CookieTempDataProvider";

    [Fact]
    public async Task SettingTempData_WritesNoTempDataCookie()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        using var response = await client.PostAsync(TempDataStorageProbeController.SetPath, content: null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(
            SetCookiePairs(response).Select(pair => pair.Split('=', 2)[0]),
            name => name.StartsWith(CookieTempDataName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TempData_SurvivesTheRedirect_CarryingOnlyTheSessionCookie()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        using var posted = await client.PostAsync(TempDataStorageProbeController.SetPath, content: null);
        var cookies = SetCookiePairs(posted);

        using var request = new HttpRequestMessage(HttpMethod.Get, TempDataStorageProbeController.ReadPath);
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        using var read = await client.SendAsync(request);

        Assert.Equal(
            TempDataStorageProbeController.PayloadLength.ToString(),
            await read.Content.ReadAsStringAsync());
    }

    // The point of the exercise: whatever the banner's size, it costs the browser nothing.
    [Fact]
    public async Task TempData_AddsNothingToTheCookiesTheBrowserSendsBack()
    {
        using var host = await BuildHostAsync();
        var client = host.GetTestClient();

        using var posted = await client.PostAsync(TempDataStorageProbeController.SetPath, content: null);
        var cookieBytes = SetCookiePairs(posted).Sum(pair => pair.Length);

        Assert.True(
            cookieBytes < 1024,
            $"A {TempDataStorageProbeController.PayloadLength}-character flash message put "
            + $"{cookieBytes} bytes of cookies on the client.");
    }

    // "name=value; Path=/; HttpOnly" -> "name=value": what the browser sends back up.
    private static List<string> SetCookiePairs(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';', 2)[0]).ToList()
            : [];

    private static async Task<IHost> BuildHostAsync()
    {
        var builder = WebApplication.CreateBuilder();

        // The registration under test. Everything else here exists only to give it a request
        // to act on.
        builder.AddCpdCoreWeb();

        builder.WebHost.UseTestServer();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession();
        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(TempDataStorageProbeController).Assembly);

        // AddCpdCoreWeb installs a fallback policy requiring an authenticated user, and the
        // flash-message flow only exists for signed-in administrators anyway. A scheme that
        // always succeeds satisfies it without dragging DfE Sign-In into the test.
        builder.Services.AddAuthentication(AlwaysSignedInHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, AlwaysSignedInHandler>(
                AlwaysSignedInHandler.SchemeName, _ => { });

        var app = builder.Build();
        app.UseSession();
        app.UseRouting();
        app.MapControllers();

        await app.StartAsync();
        return app;
    }

    private sealed class AlwaysSignedInHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "AlwaysSignedIn";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "probe")], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}

// Stands in for any controller that sets a flash message and redirects — the shape every admin
// action on the service uses. Top-level rather than nested: MVC's controller discovery tests
// Type.IsPublic, which a nested public type does not satisfy.
public sealed class TempDataStorageProbeController : Controller
{
    public const string SetPath = "/temp-data-storage-probe/set";
    public const string ReadPath = "/temp-data-storage-probe/read";

    // Comfortably past a single cookie's 4 KB ceiling, so a cookie-backed provider would have
    // to chunk it and the leak is unmissable.
    public const int PayloadLength = 6000;

    [HttpPost(SetPath)]
    public IActionResult Set()
    {
        TempData["Probe"] = new string('x', PayloadLength);
        return Redirect(ReadPath);
    }

    [HttpGet(ReadPath)]
    public IActionResult Read() =>
        Content(((TempData["Probe"] as string)?.Length ?? 0).ToString(), "text/plain");
}
