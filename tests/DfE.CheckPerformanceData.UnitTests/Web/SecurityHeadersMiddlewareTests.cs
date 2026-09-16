using DfE.CheckPerformanceData.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The response-header set was most of a good one — HSTS, a content-security policy and
// X-Frame-Options were already there — with a few gaps a security assessment picked up. These
// are cheap headers that only help if they are on every response, so they are set in one place
// rather than per-controller, and asserted here rather than left to a scan to notice.
public sealed class SecurityHeadersMiddlewareTests
{
    private static IHostEnvironment Env(string name)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(name);
        return env;
    }

    private static readonly IHostEnvironment Production = Env(Environments.Production);

    private static async Task<HttpContext> Run(
        string method = "GET",
        Action<HttpContext>? arrange = null,
        IHostEnvironment? env = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        arrange?.Invoke(context);

        var sut = new SecurityHeadersMiddleware(_ => Task.CompletedTask, env ?? Production);
        await sut.InvokeAsync(context);
        return context;
    }

    // Stops a browser second-guessing a declared content type. It matters here because the
    // storage browser and the content pipeline both serve files an author supplied.
    [Fact]
    public async Task Nosniff_IsSet()
    {
        var context = await Run();

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
    }

    // Without this the full URL — window and request identifiers included — travels to any
    // third-party origin in the Referer header. strict-origin-when-cross-origin keeps
    // same-origin navigation intact and sends only the origin outward.
    [Fact]
    public async Task ReferrerPolicy_IsSetToStrictOriginWhenCrossOrigin()
    {
        var context = await Run();

        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"]);
    }

    // The service needs none of these, and denying them costs nothing.
    [Theory]
    [InlineData("camera=()")]
    [InlineData("microphone=()")]
    [InlineData("geolocation=()")]
    public async Task PermissionsPolicy_DeniesHardwareItDoesNotUse(string directive)
    {
        var context = await Run();

        Assert.Contains(directive, context.Response.Headers["Permissions-Policy"].ToString());
    }

    // The method has no legitimate use in this service. Cross-site tracing is already closed by
    // HttpOnly cookies and modern browsers, so this is removing a surface rather than a fix.
    [Fact]
    public async Task Trace_IsRefused()
    {
        var context = await Run("TRACE");

        Assert.Equal(StatusCodes.Status405MethodNotAllowed, context.Response.StatusCode);
    }

    [Fact]
    public async Task Trace_DoesNotReachTheRestOfThePipeline()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "TRACE";
        var reached = false;

        var sut = new SecurityHeadersMiddleware(_ => { reached = true; return Task.CompletedTask; }, Production);
        await sut.InvokeAsync(context);

        Assert.False(reached);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("HEAD")]
    public async Task OrdinaryMethods_AreUntouched(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        var reached = false;

        var sut = new SecurityHeadersMiddleware(_ => { reached = true; return Task.CompletedTask; }, Production);
        await sut.InvokeAsync(context);

        Assert.True(reached);
        Assert.NotEqual(StatusCodes.Status405MethodNotAllowed, context.Response.StatusCode);
    }

    // The policy was already here and is not being changed; this pins that folding the other
    // headers in did not drop it.
    [Fact]
    public async Task ContentSecurityPolicy_IsStillSet()
    {
        var context = await Run();

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("form-action 'self'", csp);
    }

    // Setting a header twice produces two of it. Something upstream may already have supplied
    // one, and a duplicated security header is a header a browser may disagree with us about.
    [Fact]
    public async Task AHeaderAlreadySet_IsNotDuplicated()
    {
        var context = await Run(arrange: c => c.Response.Headers["X-Content-Type-Options"] = "nosniff");

        Assert.Single(context.Response.Headers["X-Content-Type-Options"]);
    }

    // The QA site was ranking in the top Google results for the service (#444). Anonymous pages
    // — Home, Search, content, Privacy — are crawlable on every host, and nothing told a crawler
    // to stay away. A header rather than a meta tag: it covers static assets, PDFs and JSON as
    // well as HTML, and it is the one signal that removes a page already in the index.
    [Theory]
    [InlineData("Development")]
    [InlineData("Review")]
    [InlineData("QA")]
    [InlineData("Preproduction")]
    public async Task NonProduction_TellsCrawlersNotToIndex(string environment)
    {
        var context = await Run(env: Env(environment));

        Assert.Equal("noindex, nofollow", context.Response.Headers["X-Robots-Tag"]);
    }

    // Keyed on !IsProduction rather than a list of hidden names, so an environment nobody
    // thought to list defaults to hidden.
    [Fact]
    public async Task AnUnknownEnvironment_IsHidden()
    {
        var context = await Run(env: Env("Staging2"));

        Assert.Equal("noindex, nofollow", context.Response.Headers["X-Robots-Tag"]);
    }

    [Fact]
    public async Task Production_DoesNotSetTheRobotsHeader()
    {
        var context = await Run(env: Production);

        Assert.False(context.Response.Headers.ContainsKey("X-Robots-Tag"));
    }
}
