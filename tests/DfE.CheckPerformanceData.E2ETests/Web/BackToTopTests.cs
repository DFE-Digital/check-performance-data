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
//   * Reveal timing follows the GDS guidance rather than firing on the first
//     nudge of the wheel: the reader has to travel nearly a whole viewport
//     before the link appears, and pages that fit (or nearly fit) the viewport
//     never show it at all.
//
// Uses /wiki/wiki-sandbox because it's the sample seeded specifically for this kind
// of layout check (long body, Wiki.cshtml render path). The behaviour is layout-
// agnostic though — every Content/Wiki page picks the same partial + CSS up.
// /guidance/short-page is its counterpart: a one-paragraph sample that fits the
// viewport, used to pin the "never show on a short page" half of the contract.
[Collection("E2E")]
public sealed class BackToTopTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    private const string TargetPath = "/wiki/wiki-sandbox";

    private const string ShortPagePath = "/guidance/short-page";

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

        // 1) Just past the reveal threshold the link should be fully faded in, pinned
        // ~50px above the viewport bottom, in the left half of the viewport. 0.95 of a
        // viewport is deliberately a shade past the 0.9 reveal point and still well
        // short of the bottom of the article, where the sticky container releases into
        // the flow and this geometry stops applying.
        await Page.EvaluateAsync("window.scrollTo(0, Math.round(window.innerHeight * 0.95))");
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

    // GDS guidance for this component: "Avoid using this component on short pages or on
    // pages designed to fit the entire viewport." A page the reader can take in without
    // scrolling has nothing to go back to the top of, so the link must stay away for the
    // whole life of the page — including at the very bottom of what little scroll exists.
    [Fact]
    public async Task OnAShortPage_LinkNeverReveals_NotEvenAtTheBottomOfTheDocument()
    {
        await Page.SetViewportSizeAsync(1280, 900);
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}{ShortPagePath}");
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        await Page.Locator(".app-back-to-top__link").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await WaitForEnhancementAsync();

        // Guard the fixture itself. If someone lengthens the seeded short page this test
        // would quietly stop testing anything, so fail loudly on that instead: the page
        // must have less than a viewport of scroll beyond the fold to be a valid subject.
        var scrollable = await Page.EvaluateAsync<int>(
            "() => Math.round(document.documentElement.scrollHeight - window.innerHeight)");
        Assert.True(scrollable < 900,
            $"/guidance/short-page is meant to fit the viewport but has {scrollable}px of scroll beyond it — " +
            "shorten the seeded body or this test proves nothing");

        // Bottom of the document is the most favourable position for a reveal — if the
        // link stays hidden here it stays hidden everywhere on this page.
        await ScrollToAsync("document.documentElement.scrollHeight");

        var state = await Page.EvaluateAsync<RevealSnap>(RevealSnapScript);
        Assert.False(state.HasVisibleClass,
            "back-to-top must not reveal on a page that fits the viewport");
        Assert.Equal("0", state.Opacity);
        Assert.Equal("hidden", state.Visibility);
    }

    // The reveal threshold is proportional to the viewport, not a fixed pixel nudge:
    // the reader has to travel nearly a full screen before the link appears. Pinning
    // both sides of the boundary keeps a future tweak from sliding it back down to a
    // scroll-and-it-pops-up value.
    [Fact]
    public async Task OnALongPage_StaysHidden_UntilNearlyAViewportHasBeenScrolled()
    {
        await Page.SetViewportSizeAsync(1280, 900);
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}{TargetPath}");
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        await Page.Locator(".app-back-to-top__link").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await WaitForEnhancementAsync();

        // Half a viewport in — the reader is still reading the first screenful of the
        // article, so the link has no business being on screen yet.
        await ScrollToAsync("Math.round(window.innerHeight / 2)");

        var halfway = await Page.EvaluateAsync<RevealSnap>(RevealSnapScript);
        Assert.False(halfway.HasVisibleClass,
            "back-to-top revealed after half a viewport of scroll — the threshold is too eager");

        // A whole viewport in — the first screenful is behind the reader, so the link
        // is now earning its place.
        await ScrollToAsync("window.innerHeight");

        var fullViewport = await Page.EvaluateAsync<RevealSnap>(RevealSnapScript);
        Assert.True(fullViewport.HasVisibleClass,
            "back-to-top should be revealed once a full viewport has been scrolled");
    }

    // A tall browser window used to lose the link entirely. The reveal threshold was a
    // fraction of the viewport, so a big monitor was penalised twice over: the bar went up
    // while the same document's scrollable distance went down, and pages that comfortably
    // showed the link on a laptop became unreachable. Drive a window taller than the
    // article's scrollable distance and confirm the link is still reachable.
    [Fact]
    public async Task OnATallWindow_LinkIsStillReachable_WhenThePageOutrunsTheViewportOnlyModestly()
    {
        await Page.SetViewportSizeAsync(1440, 1300);
        var response = await Page.GotoAsync($"{_fixture.BaseUrl}{TargetPath}");
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        await Page.Locator(".app-back-to-top__link").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await WaitForEnhancementAsync();

        // Guard the premise: on this window the page must have LESS than a viewport of
        // scroll, which is the shape that used to be unreachable. If seeded content grows
        // past that the test stops covering the regression, so fail loudly instead.
        var scrollable = await Page.EvaluateAsync<int>(
            "() => Math.round(document.documentElement.scrollHeight - window.innerHeight)");
        Assert.InRange(scrollable, 1, 1299);

        await ScrollToAsync("document.documentElement.scrollHeight");

        var state = await Page.EvaluateAsync<RevealSnap>(RevealSnapScript);
        Assert.True(state.HasVisibleClass,
            $"back-to-top must stay reachable on a tall window (scrollable={scrollable}px, viewport=1300px)");
    }

    // Wait for the progressive enhancement to claim the page AND for the resulting fade
    // to finish. The module sets the marker on <html>, which switches the CSS from the
    // no-JS always-visible fallback to the hidden-until-revealed state — but that switch
    // is a 0.3s opacity transition, so a read taken the moment the marker appears catches
    // the link partway through fading out. Settling on the resting hidden state first is
    // what makes a later "still hidden" assertion mean something.
    private Task WaitForEnhancementAsync() =>
        Page.WaitForFunctionAsync(@"() => {
            if (document.documentElement.getAttribute('data-back-to-top-init') !== 'true') return false;
            const c = document.querySelector('.app-back-to-top');
            if (!c) return false;
            const cs = getComputedStyle(c);
            return cs.opacity === '0' && cs.visibility === 'hidden';
        }");

    // Scroll, then hold until "the offset has landed AND the module has had its say
    // about it". Two things make this harder than awaiting a single window.scrollTo:
    //
    //   * The module reacts inside requestAnimationFrame, so a read taken straight after
    //     the scroll can still be showing the previous frame's answer.
    //   * Revealing the link changes the document height (the hidden container collapses
    //     to height 0 and its margin with it), which moves the maximum scroll offset
    //     underneath a scroll-to-the-bottom request.
    //
    // So re-issue the scroll every frame and wait for the offset to hold still for three
    // consecutive frames. Settling on a stable offset is what both halves need, and it
    // works for the negative assertions too — "still hidden" has no state change to await,
    // and a fixed sleep would otherwise be the only option there.
    private Task ScrollToAsync(string yExpression) =>
        Page.EvaluateAsync($@"() => new Promise((resolve, reject) => {{
            let last = -1, stable = 0, frames = 0;
            const step = () => {{
                window.scrollTo(0, {yExpression});
                if (window.scrollY === last) {{
                    if (++stable === 3) return resolve();
                }} else {{
                    stable = 0;
                    last = window.scrollY;
                }}
                if (++frames > 300) return reject(new Error('scroll offset never settled'));
                requestAnimationFrame(step);
            }};
            requestAnimationFrame(step);
        }})");

    private const string RevealSnapScript = @"() => {
        const c = document.querySelector('.app-back-to-top');
        const cs = getComputedStyle(c);
        return {
            hasVisibleClass: c.classList.contains('is-visible'),
            opacity: cs.opacity,
            visibility: cs.visibility
        };
    }";

    // Playwright's EvaluateAsync<T> instantiates T via reflection with a parameterless
    // constructor, so these snapshot DTOs are plain settable classes (not positional records).
    public sealed class RevealSnap
    {
        public bool HasVisibleClass { get; set; }
        public string Opacity { get; set; } = "";
        public string Visibility { get; set; } = "";
    }

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
