using System.Globalization;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.WindowAdmin;

/// <summary>
/// #800: the per-window rules page end to end. The seeded KS4 June window opens today, so its
/// PupilData exercise start date — the anchor the page resolves window-relative rule dates
/// against — is the run date. A seeded cutoff such as "1 September, 3 years before the window
/// year" must therefore render as 1 September of (run year - 3), never a fixed calendar date,
/// and branches whose checkingWindowType condition cannot match this window are hidden.
/// </summary>
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class WindowRulesPageTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4 June window (DevDataSeeder.KeyStage4JuneCheckingWindowId).
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    private string RulesUrl => $"{Fixture.BaseUrl}/admin/windows/{SeededWindowId}/rules";

    // The page is gated on the manage-window and rules-config sections, so the collection's
    // default editor principal 404s on it. Switch to admin for this class and hand the
    // collection's editor cookie back afterwards, as NextOpportunityTests does.
    public override async Task InitializeAsync()
    {
        await AuthHelpers.ImpersonateAsAdminAsync(Fixture);
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await AuthHelpers.ImpersonateAsEditorAsync(Fixture);
    }

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task Page_ShowsTheWindowAnchorDate_AndResolvesWindowRelativeDates()
    {
        await Page.GotoAsync(RulesUrl);

        await Expect(Page.Locator("h1.govuk-heading-xl")).ToHaveTextAsync("Key Stage 4 June rules");

        // The seeded window opens today, so the anchor row shows the run date.
        Assert.Equal(
            DateTime.Today.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture),
            await HeaderRowValueAsync("Pupil data start date"));

        // A seeded window-relative cutoff resolves against the run year, and the row keeps the
        // form name so an admin can see it is not a fixed calendar date.
        var expectedCutoff = new DateOnly(DateTime.Today.Year - 3, 9, 1)
            .ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
        var branch = BranchRow("EAL-ACC-NEWADM-2022-ALL");
        await Expect(branch).ToHaveCountAsync(1);
        var condition = await branch.Locator("ul.govuk-list--bullet").First.InnerTextAsync();
        Assert.Contains($"schoolAdmissionDate is greater than or equal to {expectedCutoff}", condition);
        Assert.Contains("1 September, 3 years before the window year", condition);
    }

    [Fact]
    public async Task Branches_For_Other_Window_Types_Are_Hidden()
    {
        await Page.GotoAsync(RulesUrl);

        // The same outcomes carry KS2 branches with the same ids' siblings; only the branches
        // that can match KS4June are listed for this window.
        await Expect(BranchRow("EAL-ACC-NEWADM-2022-ALL")).ToHaveCountAsync(1);
        await Expect(BranchRow("EAL-ACC-KS2-ALL")).ToHaveCountAsync(0);
        await Expect(BranchRow("AFE-KS2")).ToHaveCountAsync(0);
        await Expect(BranchRow("AFE-PRE2023")).ToHaveCountAsync(1);
    }

    private ILocator BranchRow(string branchId) =>
        Page.Locator(".govuk-summary-list__row").Filter(
            new() { Has = Page.Locator(".govuk-summary-list__key", new() { HasText = branchId }) });

    private async Task<string> HeaderRowValueAsync(string key) =>
        (await Page.Locator(".govuk-summary-list__row").Filter(
            new() { Has = Page.Locator(".govuk-summary-list__key", new() { HasText = key }) })
            .Locator(".govuk-summary-list__value").InnerTextAsync()).Trim();
}
