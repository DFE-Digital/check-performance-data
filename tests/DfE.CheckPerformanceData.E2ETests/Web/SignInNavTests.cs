using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// Round-trip coverage for the split-button sign-in cluster in _Layout.cshtml:
// anonymous → dropdown → impersonate as CMS admin → sign-out → anonymous.
//
// The unit-level LayoutRenderTests pin the Razor source (the branches exist).
// This test runs the same scenarios through real HTTP + Chromium to catch
// regressions where the branches exist but the integration is wrong — e.g. the
// dropdown JS doesn't toggle, the /dev/impersonate/clear endpoint doesn't
// actually delete the cookie, the claims transformer doesn't apply the editor
// role, or the server-side rendering misreads the cookie value.
[Collection("E2E")]
[Trait("Category", "W0")]
public sealed class SignInNavTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task SignInCluster_RoundTrips_ThroughImpersonationAndSignOut()
    {
        try
        {
            await Page.GotoAsync($"{_fixture.BaseUrl}/");

            // 1. Anonymous: "Sign in" link visible, caret button visible, dropdown hidden.
            var signInLi = Page.Locator(".sign-in-nav-item");
            await Expect(signInLi).ToContainTextAsync("Sign in");

            var toggle = Page.Locator(".sign-in-nav-item__toggle");
            await Expect(toggle).ToBeVisibleAsync();

            var menu = Page.Locator("#sign-in-dropdown-menu");
            await Expect(menu).ToBeHiddenAsync();

            // 2. Click caret → dropdown opens, "As CMS admin" visible.
            await toggle.ClickAsync();
            await Expect(menu).ToBeVisibleAsync();
            await Expect(menu).ToContainTextAsync("As CMS admin");

            // 3. Click "As CMS admin" → impersonation cookie set, page reloads, nav flips.
            await menu.Locator("a", new() { HasTextString = "As CMS admin" }).ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await Expect(signInLi).ToContainTextAsync("Sign out (impersonating CMS admin)");
            // Caret + dropdown disappear once a sign-out is showing.
            await Expect(toggle).ToHaveCountAsync(0);

            // 4. Click sign-out (the only anchor in the cluster now). The href targets
            //    /dev/impersonate/clear, which removes the cookie outright rather than
            //    leaving a synthetic "user" principal authenticated.
            var signOutLink = signInLi.Locator("a").First;
            await Expect(signOutLink).ToHaveAttributeAsync("href", "/dev/impersonate/clear");
            await signOutLink.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // 5. Back to anonymous: "Sign in" + caret + dropdown.
            await Expect(signInLi).ToContainTextAsync("Sign in");
            await Expect(toggle).ToBeVisibleAsync();
            await Expect(menu).ToBeHiddenAsync();

            // 6. Cookie really gone (not just flipped to "user").
            var cookies = await Page.Context.CookiesAsync();
            Assert.DoesNotContain(cookies, c => c.Name == "cypd-dev-impersonation");
        }
        finally
        {
            // Restore the fixture-level editor cookie so subsequent tests in the
            // collection can still seed. ImpersonateAsEditorAsync overwrites both the
            // server cookie and the shared TestHttpClients.ImpersonationCookieHeader
            // that every seed HttpClient request consults. Calling
            // ClearImpersonationAsync here would null that static and break every
            // editor-gated seed call in the rest of the suite.
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
