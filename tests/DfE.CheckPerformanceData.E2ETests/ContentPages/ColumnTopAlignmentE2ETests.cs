using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// A region with a menu or a search box in one column and text in the other should start both
// columns on the same line. The side navigation and the search form each bring space of their
// own above them, which used to push that column's first words 20 to 30 pixels below the other's.
//
// The pages of the in-app guide are laid out exactly this way and exist wherever this suite runs:
// its home page starts its narrow column with a side navigation, and every other page with a
// search box.
[Collection("E2E")]
public sealed class ColumnTopAlignmentE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string GuideHome = "/help/how-to-use-the-cms";

    private async Task<(float Narrow, float Wide)> FirstWordsAsync(string path, string narrowSelector)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}{path}");

        var row = Page.Locator(".cpb-content > .govuk-grid-row").First;
        var narrow = await row.Locator($".govuk-grid-column-one-third {narrowSelector}").First.BoundingBoxAsync();
        var wide = await row.Locator(".govuk-grid-column-two-thirds p").First.BoundingBoxAsync();
        Assert.NotNull(narrow);
        Assert.NotNull(wide);
        return (narrow.Y, wide.Y);
    }

    [Fact]
    public async Task ASearchBox_StartsLevelWithTheTextBesideIt()
    {
        var (narrow, wide) = await FirstWordsAsync($"{GuideHome}/for-editors", "label");

        Assert.True(Math.Abs(narrow - wide) <= 2,
            $"the search box label starts at {narrow} and the text beside it at {wide}");
    }

    // Measured on the words of the first link, not its box: the link keeps its padding so that it
    // stays easy to hit, and the words inside are what the eye lines up.
    [Fact]
    public async Task ASideNavigation_StartsLevelWithTheTextBesideIt()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}{GuideHome}");

        var tops = await Page.EvaluateAsync<float[]>(@"() => {
            const row = document.querySelector('.cpb-content > .govuk-grid-row');
            const top = (el) => { const r = document.createRange(); r.selectNodeContents(el); return r.getBoundingClientRect().top; };
            return [
                top(row.querySelector('.govuk-grid-column-one-third .moj-side-navigation a')),
                top(row.querySelector('.govuk-grid-column-two-thirds p')),
            ];
        }");

        Assert.True(Math.Abs(tops[0] - tops[1]) <= 3,
            $"the first link's words start at {tops[0]} and the text beside them at {tops[1]}");
    }
}
