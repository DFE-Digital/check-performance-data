using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#297780 "Include hand-off conflict": the one-request-per-pupil rule still applies when the
// Add journey's duplicate-check page hands off to the Include journey. If a ChangeRequest has
// already been submitted for the matched pupil, "Include this pupil" / "Switch to include" must
// re-render the duplicate-check page with the GDS conflict banner + error summary + field error
// (identical to the PupilSearch conflict) and must NOT redirect to the Include evidence page.
//
// These browser tests drive the seeded Kingsmead School pupil blobs against the KS4June window:
//   * non-included "Bob Johnson"  born 02/02/2010, UPN A860407000202B (index 201) -> the single
//     non-included match -> "Include this pupil" hand-off
//   * "Casey Carter" born 15/03/2010 (index 300's deliberate same-name pair, one included, one
//     not) -> the Multiple branch -> "Switch to include" on the non-included row
//
// Each test seeds a conflicting ChangeRequest first (self = the impersonated user for the
// self-submitted message; a random user for the colleague-named message) and cleans up any
// stale DEV-* requests so an earlier test's conflict can't poison this one.
[Collection("E2E")]
public sealed class IncludeHandoffConflictTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4June window (DevDataSeeder.KeyStage4JuneCheckingWindowId), which Kingsmead's
    // pupil blobs are uploaded against.
    private static readonly Guid Ks4JuneWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // The synthetic impersonation user's NameIdentifier (DevImpersonationTicketBuilder).
    private static readonly Guid ImpersonatedUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // The dev pipeline runner hardcodes SubmittedByName to "Dev Harness".
    private const string DevHarnessName = "Dev Harness";

    // Non-included pupil single-match (AddPupilDuplicateCheckTests): "Bob Johnson" 02/02/2010.
    private const string SingleSurname = "Johnson";
    private const string SingleFirstName = "Bob";
    private const string SingleUpn = "A860407000202B";

    // Deliberate same-name/DOB pair "Casey Carter" 15/03/2010 (one included, one not) -> the
    // Multiple branch; conflict seeded against the non-included row's UPN.
    private const string MultipleFirstName = "Casey";
    private const string MultipleSurname = "Carter";
    private const string MultipleUpn = "A8604078002B";

    // ── US1: self-submitted conflict blocks the Include-this-pupil hand-off ──

    [RetryFact(3)]
    public async Task SelfSubmittedConflict_IncludeThisPupil_ReRendersDuplicateCheck()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        var reference = await SeedHelpers.SeedConflictRequestAsync(
            Fixture.SeedClient, ImpersonatedUserId, SingleUpn, DevHarnessName, Ks4JuneWindowId);

        await StartAddJourneyAsync();
        await FillLearnerDetailsAsync(SingleFirstName, SingleSurname, day: "2", month: "2", year: "2010", sex: "M");

        // The single non-included match offers the include hand-off.
        var includeButton = Page.GetByRole(AriaRole.Button, new() { Name = "Include this pupil" });
        await Expect(includeButton).ToBeVisibleAsync();
        await includeButton.ClickAsync();

        // Blocked: the duplicate-check page re-renders with the conflict surface, NOT a redirect.
        Assert.Contains("/duplicate-check", Page.Url);
        Assert.DoesNotContain("/page/evidence", Page.Url);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("This pupil may already be on the roll");

        var banner = Page.Locator(".moj-alert--warning").First;
        await Expect(banner).ToBeVisibleAsync();
        var bannerText = await banner.InnerTextAsync();
        Assert.Contains("You have already submitted", bannerText);
        Assert.DoesNotContain(DevHarnessName, bannerText);

        await AssertConflictErrorSurfaceAsync(reference);
        // FR-006: the outcome list is still rendered for the blocked hand-off.
        await Expect(Page.Locator("table.govuk-table")).ToContainTextAsync("Johnson, Bob");
    }

    // ── US2: a colleague's conflict blocks the Switch-to-include hand-off ────

    [RetryFact(3)]
    public async Task OtherSubmittedConflict_SwitchToInclude_ShowsColleagueNameAndBlocksRedirect()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        var reference = await SeedHelpers.SeedConflictRequestAsync(
            Fixture.SeedClient, Guid.NewGuid(), MultipleUpn, DevHarnessName, Ks4JuneWindowId);

        await StartAddJourneyAsync();
        await FillLearnerDetailsAsync(MultipleFirstName, MultipleSurname, day: "15", month: "3", year: "2010", sex: "F");

        var switchButton = Page.GetByRole(AriaRole.Button, new() { Name = "Switch to include" });
        await Expect(switchButton).ToBeVisibleAsync();
        await switchButton.ClickAsync();

        Assert.Contains("/duplicate-check", Page.Url);
        Assert.DoesNotContain("/page/evidence", Page.Url);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("This pupil may already be on the roll");

        var banner = Page.Locator(".moj-alert--warning").First;
        await Expect(banner).ToBeVisibleAsync();
        var bannerText = await banner.InnerTextAsync();
        Assert.Contains("Your colleague", bannerText);
        Assert.Contains(DevHarnessName, bannerText);

        await AssertConflictErrorSurfaceAsync(reference);
        // FR-006: the outcome list (both rows) is still rendered.
        var tableText = await Page.Locator("table.govuk-table").InnerTextAsync();
        Assert.Contains("Carter, Casey", tableText);
        Assert.Contains("15/03/2010", tableText);
    }

    // ── helpers (mirror AddPupilDuplicateCheckTests) ────────────────────────

    // The conflict error surface: a GOV.UK error summary with the summary message plus a
    // field-level error, and a link to the already-submitted request in the banner and summary.
    private async Task AssertConflictErrorSurfaceAsync(string reference)
    {
        var summary = Page.Locator(".govuk-error-summary");
        await Expect(summary).ToBeVisibleAsync();
        var summaryText = await summary.InnerTextAsync();
        Assert.Contains("A request has already been submitted for this pupil", summaryText);

        await Expect(Page.Locator(".govuk-error-message").First).ToContainTextAsync("Choose another pupil");

        // The seeded reference's "view submitted request" links (banner + summary) both point at
        // the dev-host AmendmentRequests page for this window.
        var links = Page.GetByRole(AriaRole.Link, new() { Name = "View submitted request (opens in new tab)" });
        await Expect(links.First).ToBeVisibleAsync();
        var href = await links.First.GetAttributeAsync("href");
        Assert.Contains(Ks4JuneWindowId.ToString(), href);
        Assert.Contains($"/AmendmentRequests/{reference}/view", href);
    }

    private async Task StartAddJourneyAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Ks4JuneWindowId}");
        await Page.Locator("input[name='SelectedWhatToChange'][value='Add']").CheckAsync(new() { Force = true });
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/learner-details");
    }

    private async Task FillLearnerDetailsAsync(string firstName, string lastName, string day, string month, string year, string sex)
    {
        await Page.Locator("#q_first_name").FillAsync(firstName);
        await Page.Locator("#q_last_name").FillAsync(lastName);
        await FillDateAsync("date-of-birth", day, month, year);
        await Page.Locator($"input[name='q_sex'][value='{sex}']").CheckAsync(new() { Force = true });
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/duplicate-check");
    }

    private async Task FillDateAsync(string questionId, string day, string month, string year)
    {
        var baseId = $"q_{questionId.Replace("-", "_")}";
        await Page.Locator($"#{baseId}_day").FillAsync(day);
        await Page.Locator($"#{baseId}_month").FillAsync(month);
        await Page.Locator($"#{baseId}_year").FillAsync(year);
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
}