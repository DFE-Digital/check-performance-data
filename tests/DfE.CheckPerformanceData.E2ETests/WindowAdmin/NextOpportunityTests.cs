using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.WindowAdmin;

/// <summary>
/// AB#298317: the window next-opportunity edit page end to end — surfaced on the window Summary,
/// edited via a GOV.UK date input, persisted, shown as month + year, and clearable. Mirrors
/// TurnaroundCommitmentTests.
/// </summary>
[Collection("E2E")]
public sealed class NextOpportunityTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4 June window (see SeedCheckingWindows in DevDataSeeder) — seeded with no date.
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    private const string NotSetPlaceholder = "Not set";

    private string SummaryUrl => $"{Fixture.BaseUrl}/admin/windows/summary/{SeededWindowId}";
    private string EditUrl => $"{Fixture.BaseUrl}/admin/windows/{SeededWindowId}/next-opportunity";

    [Fact]
    public async Task Summary_ShowsNextOpportunityRow_WithChangeLink()
    {
        await Page.GotoAsync(SummaryUrl);
        await Expect(Page.Locator("h1.govuk-heading-xl")).ToBeVisibleAsync();

        var row = SummaryRow();
        await Expect(row).ToHaveCountAsync(1);

        var changeLink = row.GetByRole(AriaRole.Link, new() { Name = "Change Next opportunity" });
        await Expect(changeLink).ToBeVisibleAsync();
        await Expect(changeLink).ToHaveAttributeAsync("href", $"/admin/windows/{SeededWindowId}/next-opportunity");
    }

    [Fact]
    public async Task Save_PersistsTheDate_AndSummaryShowsMonthAndYear()
    {
        await Page.GotoAsync(EditUrl);
        await FillDateAsync(1, 10, 2027);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save and continue" }).ClickAsync();

        await Page.WaitForURLAsync("**/admin/windows/summary/**");
        Assert.Equal("October 2027", await CurrentSummaryValueAsync());

        // Prefill on return — the day is kept even though the Summary hides it.
        await Page.GotoAsync(EditUrl);
        await Expect(Page.Locator("input[name='NextOpportunity.Day']")).ToHaveValueAsync("1");
        await Expect(Page.Locator("input[name='NextOpportunity.Month']")).ToHaveValueAsync("10");
        await Expect(Page.Locator("input[name='NextOpportunity.Year']")).ToHaveValueAsync("2027");
    }

    [Fact]
    public async Task EmptySubmission_IsAllowed_AndShowsNotSet()
    {
        await Page.GotoAsync(EditUrl);
        await FillDateAsync(null, null, null);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save and continue" }).ClickAsync();

        await Page.WaitForURLAsync("**/admin/windows/summary/**");
        await Expect(Page.Locator(".govuk-error-summary")).ToHaveCountAsync(0);
        Assert.Equal(NotSetPlaceholder, await CurrentSummaryValueAsync());
    }

    [Fact]
    public async Task AnImpossibleDate_IsRejected_WithAnInlineError()
    {
        await Page.GotoAsync(EditUrl);
        await FillDateAsync(31, 2, 2027);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save and continue" }).ClickAsync();

        // The GOV.UK date-input binder adds the model error; the view renders it inline (the
        // sibling admin pages have no page-level error summary component). The page redisplays
        // on the same URL rather than returning to the Summary.
        await Expect(Page.Locator(".govuk-error-message")).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(EditUrl);
    }

    private async Task FillDateAsync(int? day, int? month, int? year)
    {
        await Page.FillAsync("input[name='NextOpportunity.Day']", day?.ToString() ?? string.Empty);
        await Page.FillAsync("input[name='NextOpportunity.Month']", month?.ToString() ?? string.Empty);
        await Page.FillAsync("input[name='NextOpportunity.Year']", year?.ToString() ?? string.Empty);
    }

    private ILocator SummaryRow() =>
        Page.Locator(".govuk-summary-list__row").Filter(
            new() { Has = Page.Locator(".govuk-summary-list__key", new() { HasText = "Next opportunity" }) });

    private async Task<string> CurrentSummaryValueAsync() =>
        (await SummaryRow().Locator(".govuk-summary-list__value").InnerTextAsync()).Trim();
}
