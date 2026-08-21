using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.WindowAdmin;

/// <summary>
/// Exercises the window turnaround-commitment edit page end to end: the value is surfaced on the
/// window Summary, editable via a one-field form, persisted back to the database, and an empty
/// submission is allowed (no required validation).
/// </summary>
[Collection("E2E")]
public sealed class TurnaroundCommitmentTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4 June window (see SeedCheckingWindows in DevDataSeeder).
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    private string SummaryUrl => $"{Fixture.BaseUrl}/admin/windows/summary/{SeededWindowId}";
    private string EditUrl => $"{Fixture.BaseUrl}/admin/windows/{SeededWindowId}/turnaround-commitment";

    [Fact]
    public async Task Summary_ShowsTurnaroundCommitmentRow_WithChangeLink()
    {
        await Page.GotoAsync(SummaryUrl);
        await Expect(Page.Locator("h1.govuk-heading-xl")).ToBeVisibleAsync();

        var row = Page.Locator(".govuk-summary-list__row").Filter(
            new() { Has = Page.Locator(".govuk-summary-list__key", new() { HasText = "Turnaround commitment" }) });
        await Expect(row).ToHaveCountAsync(1);

        var changeLink = row.GetByRole(AriaRole.Link, new() { Name = "Change Turnaround commitment" });
        await Expect(changeLink).ToBeVisibleAsync();
        await Expect(changeLink).ToHaveAttributeAsync("href", $"/admin/windows/{SeededWindowId}/turnaround-commitment");
    }

    [Fact]
    public async Task EditPage_PrefillsCurrentValue()
    {
        // Seed the value this test asserts on instead of reading back whatever the Summary
        // happens to show. All four tests here share one database row, and
        // EmptySubmission_IsAllowed_AndShowsNotSet leaves it empty — at which point the Summary
        // renders "Not set", which is Summary.cshtml's placeholder for an empty column and never
        // a stored value, while the edit input correctly renders "". Comparing the two failed
        // whenever xUnit ordered the empty-submission test first. Reading the value back could
        // not have earned its keep anyway: when the row is empty the assertion is "" == "",
        // which a page that does no prefilling at all would also satisfy.
        const string expected = "within 10 working days of the window closing";

        await Page.GotoAsync(EditUrl);
        await Page.Locator("#TurnaroundCommitment").FillAsync(expected);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save and continue" }).ClickAsync();
        await Page.WaitForURLAsync("**/admin/windows/summary/**");

        await Page.GotoAsync(EditUrl);
        await Expect(Page.Locator("#TurnaroundCommitment")).ToHaveValueAsync(expected);
    }

    [Fact]
    public async Task Save_PersistsValue_AndSummaryReflectsIt()
    {
        const string expected = "updated in the Spring";

        await Page.GotoAsync(EditUrl);
        await Page.Locator("#TurnaroundCommitment").FillAsync(expected);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save and continue" }).ClickAsync();

        await Page.WaitForURLAsync("**/admin/windows/summary/**");
        Assert.Equal(expected, await CurrentSummaryValueAsync());
    }

    [Fact]
    public async Task EmptySubmission_IsAllowed_AndShowsNotSet()
    {
        await Page.GotoAsync(EditUrl);
        await Page.Locator("#TurnaroundCommitment").FillAsync(string.Empty);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save and continue" }).ClickAsync();

        await Page.WaitForURLAsync("**/admin/windows/summary/**");

        await Expect(Page.Locator(".govuk-error-summary")).ToHaveCountAsync(0);
        Assert.Equal("Not set", await CurrentSummaryValueAsync());
    }

    private async Task<string> CurrentSummaryValueAsync()
    {
        var row = Page.Locator(".govuk-summary-list__row").Filter(
            new() { Has = Page.Locator(".govuk-summary-list__key", new() { HasText = "Turnaround commitment" }) });
        return (await row.Locator(".govuk-summary-list__value").InnerTextAsync()).Trim();
    }
}