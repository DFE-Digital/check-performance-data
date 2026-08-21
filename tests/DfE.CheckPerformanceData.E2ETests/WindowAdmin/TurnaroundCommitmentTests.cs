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

    // What the Summary prints in place of an unset commitment.
    private const string NotSetPlaceholder = "Not set";

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
        // Read the value currently shown on the Summary first, so the assertion holds
        // regardless of what earlier tests left in the database.
        await Page.GotoAsync(SummaryUrl);
        var value = await CurrentSummaryValueAsync();

        // "Not set" is what the Summary prints when there is no value — a placeholder, not the
        // value itself, so the edit field is legitimately empty in that state. Comparing the two
        // directly made this test depend on running before EmptySubmission_IsAllowed_AndShowsNotSet,
        // which clears the commitment. xUnit does not order tests within a class, so whichever
        // order a run happened to pick decided whether this passed.
        var expected = value == NotSetPlaceholder ? string.Empty : value;

        await Page.GotoAsync(EditUrl);
        var input = Page.Locator("#TurnaroundCommitment");
        await Expect(input).ToHaveValueAsync(expected);
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
        Assert.Equal(NotSetPlaceholder, await CurrentSummaryValueAsync());
    }

    private async Task<string> CurrentSummaryValueAsync()
    {
        var row = Page.Locator(".govuk-summary-list__row").Filter(
            new() { Has = Page.Locator(".govuk-summary-list__key", new() { HasText = "Turnaround commitment" }) });
        return (await row.Locator(".govuk-summary-list__value").InnerTextAsync()).Trim();
    }
}