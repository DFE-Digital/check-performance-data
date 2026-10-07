using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Wiki;

// The /search heading reads the same with or without a scope: "Search results for “term”". The
// scoped pages are named only in an HTML comment straight after the heading, for anyone reading
// the page source. The scope arrives on the query string, so a hostile value must not be able to
// close that comment early or inject markup.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class ScopedSearchHeadingTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string Term = "evidence";

    private ILocator Heading => Page.Locator("h1.govuk-heading-xl");

    [Fact]
    public async Task UnscopedSearch_HeadingNamesTheTerm_AndHasNoScopeComment()
    {
        var response = await Page.GotoAsync($"{Fixture.BaseUrl}/search?q={Term}");

        await Expect(Heading).ToHaveTextAsync($"Search results for “{Term}”");
        var html = await response!.TextAsync();
        Assert.DoesNotContain("<!-- search scope:", html);
    }

    [Fact]
    public async Task ScopedSearch_HeadingDoesNotListThePages_AndACommentAfterItDoes()
    {
        var response = await Page.GotoAsync(
            $"{Fixture.BaseUrl}/search?q={Term}&scope=/guidance/16-to-19/,guidance/results-enquiries");

        await Expect(Heading).ToHaveTextAsync($"Search results for “{Term}”");
        var html = await response!.TextAsync();
        Assert.Matches(
            new Regex(@"</h1>\s*<!-- search scope: guidance/16-to-19,guidance/results-enquiries -->"),
            html);
    }

    [Fact]
    public async Task HostileScope_CannotCloseTheCommentOrInjectAScript()
    {
        var dialogs = 0;
        Page.Dialog += (_, dialog) =>
        {
            dialogs++;
            _ = dialog.DismissAsync();
        };

        var hostile = Uri.EscapeDataString("--><script>alert(1)</script>");
        var response = await Page.GotoAsync($"{Fixture.BaseUrl}/search?q={Term}&scope={hostile}");

        await Expect(Heading).ToHaveTextAsync($"Search results for “{Term}”");
        var html = await response!.TextAsync();
        Assert.Matches(new Regex(@"</h1>\s*<!-- search scope: scriptalert1/script -->"), html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Equal(0, await Page.Locator("script", new PageLocatorOptions { HasTextString = "alert(1)" }).CountAsync());
        Assert.Equal(0, dialogs);
    }
}
