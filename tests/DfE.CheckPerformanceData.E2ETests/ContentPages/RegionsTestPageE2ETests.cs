using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.ContentPages;

// A region is a row of a content page, and its layout sets the columns of the row. The "Regions
// test page" under /development-testing has a region in every layout, each column labelled with
// the layout and its place in the row ("Thirds, column 2 of 3"), so these tests can find a row by
// what it says and measure it. Like the widget test pages, it is shipped with the service and is
// the same wherever this suite runs.
[Collection("E2E")]
public sealed class RegionsTestPageE2ETests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private const string PagePath = FixtureContent.RootPath + "/regions-test-page";

    private Task OpenAsync() => Page.GotoAsync($"{Fixture.BaseUrl}{PagePath}");

    // The row whose first column carries the label, and that row's own columns. The innermost
    // such row, so that a region inside a column is found and not the region around it.
    // Matched on the whole label, because "Thirds, column 1" is also the end of "One third two
    // thirds, column 1".
    private ILocator Row(string layout) =>
        Page.Locator(".cpb-content .govuk-grid-row")
            .Filter(new() { Has = Page.Locator("strong", new() { HasTextRegex = new Regex($@"^{Regex.Escape(layout)}, column 1 of \d$") }) })
            .Last;

    private static ILocator Columns(ILocator row) => row.Locator("> div");

    private static async Task<List<LocatorBoundingBoxResult>> BoxesAsync(ILocator columns)
    {
        var boxes = new List<LocatorBoundingBoxResult>();
        foreach (var column in await columns.AllAsync())
        {
            var box = await column.BoundingBoxAsync();
            Assert.NotNull(box);
            boxes.Add(box);
        }
        return boxes;
    }

    [Theory]
    [InlineData("Single", new[] { "govuk-grid-column-full" })]
    [InlineData("Halves", new[] { "govuk-grid-column-one-half", "govuk-grid-column-one-half" })]
    [InlineData("Thirds", new[] { "govuk-grid-column-one-third", "govuk-grid-column-one-third", "govuk-grid-column-one-third" })]
    [InlineData("Quarters", new[] { "govuk-grid-column-one-quarter", "govuk-grid-column-one-quarter", "govuk-grid-column-one-quarter", "govuk-grid-column-one-quarter" })]
    [InlineData("One third two thirds", new[] { "govuk-grid-column-one-third", "govuk-grid-column-two-thirds" })]
    [InlineData("Two thirds one third", new[] { "govuk-grid-column-two-thirds", "govuk-grid-column-one-third" })]
    public async Task EachLayout_IsARowOfTheDesignSystemsColumns(string layout, string[] classes)
    {
        await OpenAsync();

        var columns = Columns(Row(layout));

        await Expect(columns).ToHaveCountAsync(classes.Length);
        for (var i = 0; i < classes.Length; i++)
        {
            await Expect(columns.Nth(i)).ToHaveClassAsync(classes[i]);
            await Expect(columns.Nth(i)).ToContainTextAsync($"{layout}, column {i + 1} of {classes.Length}");
        }
    }

    // The shares each column takes of the row, left to right.
    [Theory]
    [InlineData("Single", new[] { 1.0 })]
    [InlineData("Halves", new[] { 0.5, 0.5 })]
    [InlineData("Thirds", new[] { 1 / 3.0, 1 / 3.0, 1 / 3.0 })]
    [InlineData("Quarters", new[] { 0.25, 0.25, 0.25, 0.25 })]
    [InlineData("One third two thirds", new[] { 1 / 3.0, 2 / 3.0 })]
    [InlineData("Two thirds one third", new[] { 2 / 3.0, 1 / 3.0 })]
    public async Task EachLayout_PutsItsColumnsSideBySide_AtTheWidthsItsNameSays(string layout, double[] shares)
    {
        await OpenAsync();

        var boxes = await BoxesAsync(Columns(Row(layout)));
        var whole = boxes.Sum(b => b.Width);

        for (var i = 0; i < boxes.Count; i++)
        {
            Assert.True(Math.Abs(boxes[i].Width / whole - shares[i]) < 0.02,
                $"{layout}: column {i + 1} takes {boxes[i].Width / whole:P0} of the row, not {shares[i]:P0}");
            if (i == 0) continue;
            Assert.True(Math.Abs(boxes[i].Y - boxes[0].Y) < 2, $"{layout}: column {i + 1} is not level with the first");
            Assert.True(boxes[i].X >= boxes[i - 1].X + boxes[i - 1].Width - 1, $"{layout}: column {i + 1} overlaps the one before");
        }
    }

    // What is in a column is as wide as the column, and no wider.
    [Theory]
    [InlineData("Halves")]
    [InlineData("Quarters")]
    [InlineData("One third two thirds")]
    public async Task TheContentOfAColumn_StaysInsideIt(string layout)
    {
        await OpenAsync();

        foreach (var column in await Columns(Row(layout)).AllAsync())
        {
            var box = await column.BoundingBoxAsync();
            var line = await column.Locator("hr").BoundingBoxAsync();
            var text = await column.Locator("p").Last.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.NotNull(line);
            Assert.NotNull(text);

            Assert.True(line.X >= box.X - 1 && line.X + line.Width <= box.X + box.Width + 1, $"{layout}: a line leaves its column");
            Assert.True(text.X >= box.X - 1 && text.X + text.Width <= box.X + box.Width + 1, $"{layout}: text leaves its column");
        }
    }

    [Fact]
    public async Task AnEmptyColumn_KeepsItsSpace()
    {
        await OpenAsync();

        var columns = Columns(Row("Empty beside"));
        var boxes = await BoxesAsync(columns);

        await Expect(columns).ToHaveCountAsync(2);
        await Expect(columns.Nth(1)).ToBeEmptyAsync();
        var row = await Row("Empty beside").BoundingBoxAsync();
        Assert.NotNull(row);
        Assert.True(boxes[0].Width < row.Width * 0.55, $"the column beside the empty one is {boxes[0].Width} wide in a row of {row.Width}");
    }

    [Fact]
    public async Task ARegionInsideAColumn_SplitsThatColumn_NotThePage()
    {
        await OpenAsync();

        var outer = await BoxesAsync(Columns(Row("Outer")));
        var inner = await BoxesAsync(Columns(Row("Inner")));

        Assert.Equal(2, inner.Count);
        Assert.True(Math.Abs(inner[0].Y - inner[1].Y) < 2, "the inner columns are not side by side");
        Assert.True(inner[1].X + inner[1].Width <= outer[0].X + outer[0].Width + 1, "the inner region runs past the column it is in");
        Assert.True(Math.Abs(inner[0].Width - inner[1].Width) < 2, "the inner columns are not the same width");
    }

    [Theory]
    [InlineData("Halves")]
    [InlineData("Quarters")]
    [InlineData("Two thirds one third")]
    public async Task OnAPhone_TheColumnsStack_LeftColumnFirst(string layout)
    {
        await Page.SetViewportSizeAsync(375, 800);
        await OpenAsync();

        var boxes = await BoxesAsync(Columns(Row(layout)));

        for (var i = 1; i < boxes.Count; i++)
        {
            Assert.True(Math.Abs(boxes[i].X - boxes[0].X) < 2, $"{layout}: column {i + 1} is not under the first");
            Assert.True(boxes[i].Y >= boxes[i - 1].Y + boxes[i - 1].Height - 1, $"{layout}: column {i + 1} is not below the one before");
        }
    }

    [Fact]
    public async Task OnAPhone_ThePageDoesNotScrollSideways()
    {
        await Page.SetViewportSizeAsync(375, 800);
        await OpenAsync();

        var overflow = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        Assert.True(overflow <= 0, $"the page is {overflow}px wider than the screen");
    }
}
