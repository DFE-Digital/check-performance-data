using DfE.CheckPerformanceData.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The response-header set was most of a good one — HSTS and a content-security policy were
// already there — with a few gaps a security assessment picked up (and, until #610, no
// frame-ancestors; the app has never sent X-Frame-Options, whatever this comment once said). These
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

    private const string ProductionMetadata =
        "https://oidc.signin.education.gov.uk/.well-known/openid-configuration";

    private static IConfiguration Config(string? metadataAddress = ProductionMetadata, bool? reportOnly = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DfeSignIn:MetadataAddress"] = metadataAddress,
                ["Csp:ReportOnly"] = reportOnly?.ToString()
            })
            .Build();

    private static async Task<HttpContext> Run(
        string method = "GET",
        Action<HttpContext>? arrange = null,
        IHostEnvironment? env = null,
        IConfiguration? config = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        arrange?.Invoke(context);

        var sut = new SecurityHeadersMiddleware(_ => Task.CompletedTask, env ?? Production, config ?? Config());
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

        var sut = new SecurityHeadersMiddleware(_ => { reached = true; return Task.CompletedTask; }, Production, Config());
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

        var sut = new SecurityHeadersMiddleware(_ => { reached = true; return Task.CompletedTask; }, Production, Config());
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

    // frame-ancestors is the CSP directive that decides which sites may put this service in a
    // frame; it replaced X-Frame-Options in every current browser. The policy had none, and the
    // app has never set X-Frame-Options (the comment at the top of this file used to say it
    // did), so until #610 whether a page could be framed — the clickjacking surface — depended
    // on whatever sat in front of the app. Asserted as a whole directive rather than a
    // substring: "frame-ancestors 'self' https://other.example" would pass a Contains.
    [Fact]
    public async Task FrameAncestors_AllowsOnlyThisSite()
    {
        var context = await Run();

        var directives = context.Response.Headers["Content-Security-Policy"].ToString()
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("frame-ancestors 'self'", directives);
    }

    // A POST made after the sign-in has expired is answered with a 302 to DfE Sign-in. Chrome
    // applies form-action to every redirect a form submission follows, so with 'self' alone the
    // browser cancelled that navigation and the page simply did nothing — on every form in the
    // service. A PROD tester on Check your student data hit exactly this.
    [Fact]
    public async Task FormAction_AllowsTheDfeSignInOrigin()
    {
        var context = await Run();

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("form-action 'self' https://oidc.signin.education.gov.uk", csp);
    }

    // Only the origin is allowed, never the metadata document's path: form-action matches the
    // authorize endpoint, which is another path on the same host.
    [Fact]
    public async Task FormAction_UsesTheOriginOfTheMetadataAddressOnly()
    {
        var context = await Run(config: Config("https://test-oidc.signin.education.gov.uk/.well-known/openid-configuration"));

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.EndsWith("form-action 'self' https://test-oidc.signin.education.gov.uk", csp);
    }

    // Missing or unusable configuration must narrow the policy back to 'self', never widen it.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/openid-configuration")]
    [InlineData("ftp://oidc.example/openid-configuration")]
    public async Task FormAction_WithoutAUsableMetadataAddress_IsSelfOnly(string? metadataAddress)
    {
        var context = await Run(config: Config(metadataAddress));

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.EndsWith("form-action 'self'", csp);
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

    // --- #611 strict script-src + per-request nonce ---

    // The whole point of #611: the two allowances that let an injected inline script or an
    // injected eval string run are gone. This is the guard that fails if either returns.
    [Fact]
    public async Task ScriptSrc_CarriesNeitherUnsafeInlineNorUnsafeEval()
    {
        var context = await Run();

        var scriptSrc = Directive(context, "script-src");
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc);
        Assert.DoesNotContain("'unsafe-eval'", scriptSrc);
    }

    // The nonce replaces 'unsafe-inline': only scripts the response itself authorised run.
    [Fact]
    public async Task ScriptSrc_PublishesANonce()
    {
        var context = await Run();

        var scriptSrc = Directive(context, "script-src");
        Assert.Matches(@"'nonce-[A-Za-z0-9_-]{22}'", scriptSrc);
    }

    // A nonce that does not change between responses is a static allowance in disguise.
    [Fact]
    public async Task ScriptSrc_NonceDiffersBetweenResponses()
    {
        var first = NonceHeader(await Run());
        var second = NonceHeader(await Run());

        Assert.NotEqual(first, second);
    }

    // The TagHelper reads the nonce from Items; it must be the same value the header published.
    [Fact]
    public async Task NonceHeader_MatchesTheValueStoredOnHttpContextItems()
    {
        var context = await Run();

        var stored = Assert.IsType<string>(context.Items[CspNonce.ItemKey]);
        Assert.Contains($"'nonce-{stored}'", context.Response.Headers["Content-Security-Policy"].ToString());
    }

    // style-src is deliberately out of scope for #611 and keeps its allowance.
    [Fact]
    public async Task StyleSrc_StillAllowsUnsafeInline()
    {
        var context = await Run();

        Assert.Contains("'unsafe-inline'", Directive(context, "style-src"));
    }

    // Removing 'unsafe-inline' is not the mechanism for removing the analytics origins; the
    // tags must still be allowed to load their external files.
    [Theory]
    [InlineData("https://*.googletagmanager.com")]
    [InlineData("https://*.google-analytics.com")]
    [InlineData("https://*.analytics.google.com")]
    [InlineData("https://*.clarity.ms")]
    public async Task ScriptSrc_KeepsTheAnalyticsOrigins(string origin)
    {
        var context = await Run();

        Assert.Contains(origin, Directive(context, "script-src"));
    }

    // The rehearsal switch: report-only outside Production so breakage is observable without
    // failing pages. The directive contents (including the nonce) are identical.
    [Fact]
    public async Task ReportOnly_OutsideProduction_EmitsTheReportOnlyHeader()
    {
        var context = await Run(env: Env("Development"), config: Config(reportOnly: true));

        Assert.True(context.Response.Headers.ContainsKey("Content-Security-Policy-Report-Only"));
        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
        Assert.Contains("'nonce-", context.Response.Headers["Content-Security-Policy-Report-Only"].ToString());
    }

    // A misconfigured environment variable must never leave a deployed production site
    // unenforced: Production ignores the switch.
    [Fact]
    public async Task ReportOnly_InProduction_StillEmitsTheEnforcingHeader()
    {
        var context = await Run(env: Production, config: Config(reportOnly: true));

        Assert.True(context.Response.Headers.ContainsKey("Content-Security-Policy"));
        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task ReportOnlyDisabled_EmitsTheEnforcingHeader()
    {
        var context = await Run(env: Env("Development"));

        Assert.True(context.Response.Headers.ContainsKey("Content-Security-Policy"));
    }

    private static string Directive(HttpContext context, string name)
    {
        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        var start = csp.IndexOf(name + " ", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} must be present in the policy");
        var end = csp.IndexOf(';', start);
        return end < 0 ? csp[start..] : csp[start..end];
    }

    private static string NonceHeader(HttpContext context) =>
        System.Text.RegularExpressions.Regex.Match(
            context.Response.Headers["Content-Security-Policy"].ToString(),
            @"'nonce-([A-Za-z0-9_-]+)'").Groups[1].Value;
}
