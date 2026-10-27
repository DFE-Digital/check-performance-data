using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// #611 / FR-013: the admin and CMS surfaces are the ones most likely to grow a hand-written
// inline script, and they render through _AdminLayout rather than _Layout. Every inline script
// on them must carry the response's nonce; SeedingPageTest's violation gate then fails the test
// if anything else is blocked. External same-origin scripts need no nonce ('self' allows them).
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class CspAdminPagesTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    public static TheoryData<string> Pages => new()
    {
        "/admin",
        "/admin/pages",
        "/admin/content-blocks",
        "/admin/settings",
        "/admin/rules",
        "/admin/storage",
        "/admin/observability",
    };

    // The rules, storage, settings and observability surfaces are admin-gated, and the fixture
    // impersonates an editor. Switch to admin for the test, then put the editor back: the
    // impersonation cookie is the collection-wide fixture default.
    protected override async Task SeedAsync()
    {
        var adminCookie = await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        if (string.IsNullOrEmpty(adminCookie)) return;
        var equalsIndex = adminCookie.IndexOf('=');
        if (equalsIndex <= 0) return;
        await Context.AddCookiesAsync([new Cookie
        {
            Name = adminCookie[..equalsIndex],
            Value = adminCookie[(equalsIndex + 1)..],
            Url = Fixture.BaseUrl
        }]);
    }

    public override async Task DisposeAsync()
    {
        await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
        await base.DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task AdminPage_EveryInlineScriptCarriesTheResponseNonce(string path)    {
        var response = await Page.GotoAsync($"{Fixture.BaseUrl}{path}")
            ?? throw new InvalidOperationException($"No response from {path}.");

        Assert.Equal(200, response.Status);
        var nonce = Regex.Match(
            response.Headers["content-security-policy"], @"'nonce-([A-Za-z0-9_-]+)'").Groups[1].Value;
        Assert.NotEmpty(nonce);

        var scripts = await Page.EvaluateAsync<string?[][]>(
            "() => Array.from(document.querySelectorAll('script')).map(s => [s.nonce, s.getAttribute('src')])");

        Assert.All(
            scripts.Where(s => string.IsNullOrEmpty(s[1])),
            script => Assert.Equal(nonce, script[0]));
    }
}
