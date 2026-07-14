using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Web;

// Pins the back-to-top link contract on a CMS content page:
//   * Sticky-positioned near the viewport bottom, ~50px above it, while its
//     containing content column is in view.
//   * Universal opaque white pill — subtle shadow + light border so the button
//     reads as a button on every viewport and the text underneath (article body,
//     sidebar navigation, admin content) doesn't bleed through. Applied at all
//     widths because layouts with a left sidebar (wiki pages, admin pages) or
//     narrow viewports never actually give the sticky link an empty gutter to
//     hide in.
//   * On mobile (<= 40.0625em): additionally right-aligned because prose
//     paragraphs typically wrap short of the right edge on narrow screens, so
//     the pill obscures less of the article body than a lower-left one would.
//   * At the page bottom the link releases at the natural end of the content area
//     so it sits above the footer instead of overlapping it.
//   * Anchor href="#top" scrolls the window back to the top.
//
// Uses /wiki/wiki-sandbox because it's the sample seeded specifically for this kind
// of layout check (long body, Wiki.cshtml render path). The behaviour is layout-
// agnostic though — every Content/Wiki page picks the same partial + CSS up.
[Collection("E2E")]
[Trait("Category", "W1")]
public sealed class BackToTopTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    private const string TargetPath = "/wiki/wiki-sandbox";

    [Theory]
    [InlineData(1280, 900, "desktop")]
    [InlineData(768, 1024, "tablet")]
    public async Task PillChromePresent_OnDesktopAndTablet_ForContrastOverBodyContent(int w, int h, string label)
    {
        _ = label;
        await Page.SetViewportSizeAsync(w, h);
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}{TargetPath}");
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        await Page.Locator(".app-back-to-top__link").WaitForAsync(new() { State = WaitForSelectorState.Attached });

        var style = await Page.EvaluateAsync<StyleSnap>(@"() => {
            const link = document.querySelector('.app-back-to-top__link');
            const cs = getComputedStyle(link);
            return {
                background: cs.backgroundColor,
                boxShadow: cs.boxShadow,
                borderRadius: cs.borderRadius
            };
        }");

        // Universal pill: opaque white background, subtle shadow, rounded pill shape.
        // Applied at every viewport because wiki/admin layouts have a left sidebar
        // whose text would otherwise bleed through a transparent link.
        Assert.Equal("rgb(255, 255, 255)", style.Background);
        Assert.NotEqual("none", style.BoxShadow);
        Assert.NotEqual("0px", style.BorderRadius);
    }

    // On mobile the sticky link overlays article text (single-column layout, no left
    // gutter to hide in) so the transparent GDS treatment becomes illegible. The
    // mobile fix: opaque white pill with a subtle shadow and light border so the
    // button reads as a button and the text underneath doesn't bleed through, AND
    // align it to the right edge because prose paragraphs typically wrap short of
    // the right edge — the pill then obscures less of the body copy than a
    // lower-left one would. Pin both properties so the fix can't silently regress.
    [Fact]
    public async Task AtMobileViewport_LinkHasOpaquePill_InLowerRight_ForContrastOverBodyContent()
    {
        await Page.SetViewportSizeAsync(375, 667);
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}{TargetPath}");
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        await Page.Locator(".app-back-to-top__link").WaitForAsync(new() { State = WaitForSelectorState.Attached });

        // Scroll into content so the link is sticky-active and its bounding box is
        // pinned near the viewport bottom, then read chrome + geometry in one hop.
        await Page.EvaluateAsync("window.scrollTo(0, 800)");
        await Page.WaitForFunctionAsync(@"() => {
            const c = document.querySelector('.app-back-to-top');
            return c && c.classList.contains('is-visible') && getComputedStyle(c).opacity === '1';
        }");

        var snap = await Page.EvaluateAsync<MobilePillSnap>(@"() => {
            const link = document.querySelector('.app-back-to-top__link');
            const cs = getComputedStyle(link);
            const lr = link.getBoundingClientRect();
            return {
                background: cs.backgroundColor,
                boxShadow: cs.boxShadow,
                borderRadius: cs.borderRadius,
                linkLeft: Math.round(lr.left),
                linkRight: Math.round(lr.right),
                viewportWidth: window.innerWidth
            };
        }");

        // Pill chrome — opaque white background, rounded, with a subtle shadow.
        Assert.Equal("rgb(255, 255, 255)", snap.Background);
        Assert.NotEqual("none", snap.BoxShadow);
        Assert.NotEqual("0px", snap.BorderRadius);

        // Right-edge alignment — the link's right edge should be within a small
        // gutter of the viewport's right edge, and its left edge should be in the
        // right half of the viewport (i.e. clearly not sitting at the left).
        Assert.True(snap.ViewportWidth - snap.LinkRight < 30,
            $"pill should hug the right edge but linkRight={snap.LinkRight}, viewport={snap.ViewportWidth}");
        Assert.True(snap.LinkLeft > snap.ViewportWidth / 2,
            $"pill should be in the right half of the viewport but linkLeft={snap.LinkLeft}, viewport={snap.ViewportWidth}");
    }

    [Fact]
    public async Task HiddenAtTop_FadesInAfterScroll_ReleasesAboveFooter_ScrollsToTopOnClick()
    {
        await Page.SetViewportSizeAsync(1280, 900);
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}{TargetPath}");
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        var linkLocator = Page.Locator(".app-back-to-top__link");
        await linkLocator.WaitForAsync(new() { State = WaitForSelectorState.Attached });

        // 0) The JS enhancement should have flipped data-back-to-top-init on <html> and
        // left the link hidden at scroll = 0. WaitForFunctionAsync for both the marker
        // AND the 0.3s CSS fade-to-hidden to settle — with the script loaded via defer
        // in the layout, the initial hide fires slightly later than an inline script
        // would, so a bare EvaluateAsync could catch opacity mid-transition.
        await Page.WaitForFunctionAsync(@"() => {
            if (document.documentElement.getAttribute('data-back-to-top-init') !== 'true') return false;
            const c = document.querySelector('.app-back-to-top');
            if (!c) return false;
            const cs = getComputedStyle(c);
            return cs.opacity === '0' && cs.visibility === 'hidden';
        }");

        // 1) After ~400px of scroll the link should be fully faded in, pinned ~50px above
        // the viewport bottom, in the left half of the viewport.
        await Page.EvaluateAsync("window.scrollTo(0, 400)");
        // Wait for the class flip + the 300ms opacity transition to complete, deterministic
        // rather than a fixed sleep — WaitForFunctionAsync polls until the predicate returns
        // truthy, so a slow CI runner and a fast local dev machine both see the same barrier.
        await Page.WaitForFunctionAsync(@"() => {
            const c = document.querySelector('.app-back-to-top');
            return c && c.classList.contains('is-visible') && getComputedStyle(c).opacity === '1';
        }");

        var midway = await Page.EvaluateAsync<MidSnap>(@"() => {
            const c = document.querySelector('.app-back-to-top');
            const link = document.querySelector('.app-back-to-top__link');
            const cs = getComputedStyle(c);
            const lr = link.getBoundingClientRect();
            return {
                position: cs.position,
                opacity: cs.opacity,
                visibility: cs.visibility,
                distanceFromViewportBottom: Math.round(window.innerHeight - lr.bottom),
                linkLeft: Math.round(lr.left)
            };
        }");
        Assert.Equal("sticky", midway.Position);
        Assert.Equal("1", midway.Opacity);
        Assert.Equal("visible", midway.Visibility);
        // 50px above viewport bottom, ± 8px tolerance for line-height / rounding.
        Assert.InRange(midway.DistanceFromViewportBottom, 42, 58);
        // Left half of a 1280px viewport = bottom-LEFT, matching the ticket wording.
        Assert.True(midway.LinkLeft < 640,
            $"link should be in the left half of the viewport but linkLeft = {midway.LinkLeft}");

        // 2) At the page bottom the sticky element must release at the natural end of the
        // content area rather than sit inside the govuk-footer strip.
        await Page.EvaluateAsync("window.scrollTo(0, document.documentElement.scrollHeight)");
        // Wait until the browser has actually committed the max-scroll — cheaper than a fixed
        // sleep and doesn't flake on slow CI runners.
        await Page.WaitForFunctionAsync(
            "() => Math.abs(window.scrollY + window.innerHeight - document.documentElement.scrollHeight) < 2");

        var bottom = await Page.EvaluateAsync<BottomSnap>(@"() => {
            const link = document.querySelector('.app-back-to-top__link');
            const footer = document.querySelector('.govuk-footer');
            const lr = link.getBoundingClientRect();
            const fr = footer.getBoundingClientRect();
            return {
                linkBottom: Math.round(lr.bottom),
                footerTop: Math.round(fr.top),
                overlapsFooter: (lr.bottom > fr.top && lr.top < fr.bottom)
            };
        }");
        Assert.False(bottom.OverlapsFooter,
            $"link should not overlap the footer (linkBottom={bottom.LinkBottom}, footerTop={bottom.FooterTop})");
        Assert.True(bottom.LinkBottom <= bottom.FooterTop,
            $"link bottom {bottom.LinkBottom} should sit at or above footer top {bottom.FooterTop}");

        // 3) Anchor scrolls the window back to the top.
        await linkLocator.ClickAsync();
        await Page.WaitForFunctionAsync("() => window.scrollY === 0");
        var scrollY = await Page.EvaluateAsync<long>("() => window.scrollY");
        Assert.Equal(0, scrollY);
    }

    // Playwright's EvaluateAsync<T> instantiates T via reflection with a parameterless
    // constructor, so these snapshot DTOs are plain settable classes (not positional records).
    public sealed class AtTopSnap
    {
        public string Opacity { get; set; } = "";
        public string Visibility { get; set; } = "";
    }

    public sealed class StyleSnap
    {
        public string Background { get; set; } = "";
        public string BoxShadow { get; set; } = "";
        public string BorderRadius { get; set; } = "";
    }

    public sealed class MobilePillSnap
    {
        public string Background { get; set; } = "";
        public string BoxShadow { get; set; } = "";
        public string BorderRadius { get; set; } = "";
        public int LinkLeft { get; set; }
        public int LinkRight { get; set; }
        public int ViewportWidth { get; set; }
    }

    public sealed class MidSnap
    {
        public string Position { get; set; } = "";
        public string Opacity { get; set; } = "";
        public string Visibility { get; set; } = "";
        public int DistanceFromViewportBottom { get; set; }
        public int LinkLeft { get; set; }
    }

    public sealed class BottomSnap
    {
        public int LinkBottom { get; set; }
        public int FooterTop { get; set; }
        public bool OverlapsFooter { get; set; }
    }
}
