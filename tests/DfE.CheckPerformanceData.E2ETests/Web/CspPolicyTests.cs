using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// #611 end to end: the response really does ship a strict script-src with a nonce, every script it
// renders carries that nonce, and code that is not stamped does not run. The unit tests pin the
// policy string; only a browser proves the policy is actually enforced on a real page.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class CspPolicyTests(PlaywrightFixture fixture) : PageTest
{
    private const string PublicPath = "/";

    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task PublicPage_ScriptSrcIsStrict_AndPublishesANonce()
    {
        var csp = await ContentSecurityPolicyAsync();

        var scriptSrc = Directive(csp, "script-src");
        Assert.Contains("'self'", scriptSrc);
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc);
        Assert.DoesNotContain("'unsafe-eval'", scriptSrc);
        Assert.Matches(@"'nonce-[A-Za-z0-9_-]+'", scriptSrc);
    }

    [Fact]
    public async Task PublicPage_NonceDiffersBetweenLoads()
    {
        var first = Nonce(await ContentSecurityPolicyAsync());

        await Page.GotoAsync($"{_fixture.BaseUrl}{PublicPath}");
        var second = Nonce(await ContentSecurityPolicyAsync());

        Assert.NotEqual(first, second);
    }

    // An inline script only runs when it carries the response's nonce. External scripts are
    // allowed by 'self' and need no nonce, so a src-bearing element is not a failure - the one
    // such script on the home page is the GOV.UK Frontend module import, which the library emits
    // without a nonce by design. An inline element without the nonce is always a failure.
    [Fact]
    public async Task PublicPage_EveryInlineScriptCarriesTheResponseNonce()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}{PublicPath}");
        var expected = Nonce(await ContentSecurityPolicyAsync());

        var scripts = await Page.EvaluateAsync<string?[][]>(
            "() => Array.from(document.querySelectorAll('script')).map(s => [s.nonce, s.getAttribute('src')])");

        Assert.NotEmpty(scripts);
        var inline = scripts.Where(s => string.IsNullOrEmpty(s[1])).ToList();
        Assert.NotEmpty(inline);
        Assert.All(inline, script => Assert.Equal(expected, script[0]));
    }

    [Fact]
    public async Task RuntimeInlineScript_DoesNotRun()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}{PublicPath}");

        await Page.EvaluateAsync(
            "() => { const s = document.createElement('script'); s.textContent = 'window.__cspInjected = 1;'; document.body.appendChild(s); }");

        var injected = await Page.EvaluateAsync<object?>("() => window.__cspInjected ?? null");
        Assert.Null(injected);
    }

    // Playwright's EvaluateAsync runs through CDP's Runtime.evaluate, which is not subject to the
    // page's policy, and a script element it inserts inherits that exemption, so injecting the
    // probe from the test proves nothing. Rewrite the served HTML instead: the probe is parsed
    // with the response and carries that response's own nonce, exactly like the app's scripts.
    [Fact]
    public async Task Eval_IsBlockedForScriptThatCarriesTheNonce()
    {
        var url = $"{_fixture.BaseUrl}{PublicPath}";

        await Page.RouteAsync(url, async route =>
        {
            var response = await route.FetchAsync();
            var nonce = Nonce(response.Headers["content-security-policy"]);
            var html = await response.TextAsync();
            const string probe =
                "<script nonce=\"{0}\">try {{ eval('1+1'); window.__evalBlocked = false; }} " +
                "catch (e) {{ window.__evalBlocked = true; }}</script>";
            var rewritten = html.Replace("</body>", string.Format(probe, nonce) + "</body>", StringComparison.Ordinal);
            await route.FulfillAsync(new() { Response = response, Body = rewritten });
        });

        await Page.GotoAsync(url);

        Assert.True(
            await Page.EvaluateAsync<bool>("() => window.__evalBlocked === true"),
            "'unsafe-eval' is absent from script-src, so eval inside a correctly nonced script must throw.");
    }

    private async Task<string> ContentSecurityPolicyAsync()
    {
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}{PublicPath}")
            ?? throw new InvalidOperationException($"No response from {PublicPath}.");

        if (!response.Headers.TryGetValue("content-security-policy", out var csp))
        {
            throw new InvalidOperationException(
                $"No Content-Security-Policy header on {PublicPath} (headers: {string.Join(", ", response.Headers.Keys)}).");
        }

        return csp;
    }

    private static string Directive(string csp, string name)
    {
        var start = csp.IndexOf(name + " ", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{name} must be present in the policy: {csp}");
        var end = csp.IndexOf(';', start);
        return end < 0 ? csp[start..] : csp[start..end];
    }

    private static string Nonce(string csp) =>
        Regex.Match(csp, @"'nonce-([A-Za-z0-9_-]+)'").Groups[1].Value;
}
