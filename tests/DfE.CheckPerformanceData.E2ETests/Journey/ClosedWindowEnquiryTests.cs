using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#298317: a 16-19 window whose pupil-data exercise has closed while results enquiry runs on.
// The school is told the window has closed and when the next opportunity is, keeps its tables and
// downloads, is asked whether it wants to report an issue with an exam result, and from "Yes"
// onward the enquiry journey is the one the open-window suites already cover.
//
// The landing page is deliberately not exercised here: dev-impersonated users have no organisation
// claim and are challenged by it. LandingPageControllerTests + LandingPageViewRenderTests pin it.
[Collection("E2E")]
public sealed class ClosedWindowEnquiryTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // DevDataSeeder.ClosedPupilDataPost16CheckingWindowId — pupil data closed yesterday, results
    // enquiry open for months, NextOpportunity = 1 October next year.
    private static readonly Guid WindowId = Guid.Parse("7D3F0B21-4C8E-4A9B-9F62-1E5A8C0D3B47");

    private const string StudentCypmdId = "500001";
    private const string StudentName = "Alice Smith";
    private const string BusStudsS2024 = "GCSE (9-1) Bus. Studs:Single, QAN: 6037116X, Session: S2024";

    private string PageUrl => $"{Fixture.BaseUrl}/CheckYourPupilData/{WindowId}";

    [RetryFact(3)]
    public async Task The_school_is_told_the_window_has_closed_and_asked_only_the_enquiry_question()
    {
        await Page.GotoAsync(PageUrl);

        var body = await Page.Locator("main").InnerTextAsync();
        Assert.Contains("The 16 to 19 (pupil data closed) data checking window has closed.", body);
        Assert.Contains($"The next opportunity to review your performance data will be in October {DateTime.Now.Year + 1}.", body);
        Assert.Contains("You can still view your exam results and report any issues.", body);
        Assert.DoesNotContain("You must request any changes", body);

        // Content stays: the tables, the search and the downloads.
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Download all CSV files as a ZIP file" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".govuk-tabs")).ToBeVisibleAsync();

        // Actions shrink to the one question.
        await Expect(Page.GetByText("Would you like to report an issue with an exam result?")).ToBeVisibleAsync();
        await Expect(Page.Locator("input[name='SelectedNextStep']")).ToHaveCountAsync(2);
        await Expect(Page.Locator("input[name='SelectedNextStep'][value='ResultsEnquiry']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedNextStep'][value='SignOut']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedNextStep'][value='RequestChange']")).ToHaveCountAsync(0);
        await Expect(Page.Locator("input[name='SelectedNextStep'][value='Confirm']")).ToHaveCountAsync(0);
    }

    [RetryFact(3)]
    public async Task Yes_reaches_the_issue_chooser_and_an_enquiry_submits_as_it_does_in_an_open_window()
    {
        await Page.GotoAsync(PageUrl);
        await Page.Locator("input[name='SelectedNextStep'][value='ResultsEnquiry']").CheckAsync(new() { Force = true });
        await ContinueAsync();

        await Page.WaitForURLAsync($"**/{WindowId}/ResultIssue");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("What issue with the results do you need to report?");

        // The same result-does-not-belong journey ResultDoesNotBelongEnquiryTests walks on the
        // open window — nothing about a closed pupil-data exercise changes it.
        await Page.Locator("input[name='IssueType'][value='result-does-not-belong']").CheckAsync(new() { Force = true });
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/pupil-search/select-student");

        var search = Page.Locator("#pupil-search").First;
        await Expect(search).ToBeVisibleAsync();
        await search.FillAsync(StudentCypmdId);
        var option = Page.Locator("li[role='option']").GetByText(StudentCypmdId);
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();
        await ContinueAsync();

        await Page.WaitForURLAsync($"**/Journey/{WindowId}/result-search/select-result");
        var resultSearch = Page.Locator("#result-search").First;
        await Expect(resultSearch).ToBeVisibleAsync();
        await resultSearch.FillAsync("Bus");
        var resultOption = Page.Locator("li[role='option']").GetByText(BusStudsS2024, new() { Exact = false });
        await Expect(resultOption.First).ToBeVisibleAsync();
        await resultOption.First.ClickAsync();
        await Expect(Page.Locator("select[name='selectedResultKey'] option:checked")).ToContainTextAsync(BusStudsS2024);
        await Page.Locator("form").EvaluateAsync("form => form.requestSubmit()");

        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/additional-info");
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync($"Summary of result enquiry for {StudentName}");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{WindowId}/enquiry-confirmation");
        var panel = await Page.Locator(".govuk-panel").InnerTextAsync();
        Assert.Matches(@"CYPMD_16to19_RE_[0-9A-F]{7}", panel);
    }

    [RetryFact(3)]
    public async Task No_signs_the_school_out()
    {
        await Page.GotoAsync(PageUrl);
        await Page.Locator("input[name='SelectedNextStep'][value='SignOut']").CheckAsync(new() { Force = true });
        await ContinueAsync();

        // Impersonated sessions sign out by clearing the cookie; the header then offers "Sign in".
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("a.govuk-service-navigation__link", new() { HasText = "Sign out" })).ToHaveCountAsync(0);
        // This test's browser context is the only thing signed out — the fixture's cookie header is
        // untouched, so the next test's context re-impersonates as normal.
    }

    [RetryFact(3)]
    public async Task A_forged_amendment_answer_is_rejected_as_unanswered()
    {
        await Page.GotoAsync(PageUrl);
        // Rewrite the Yes radio's value to the option this page no longer offers, then submit.
        await Page.Locator("input[name='SelectedNextStep'][value='ResultsEnquiry']")
            .EvaluateAsync("el => { el.value = 'RequestChange'; el.checked = true; }");
        await Page.Locator("form:has(input[name='SelectedNextStep'])").EvaluateAsync("form => form.requestSubmit()");

        // The page renders the GOV.UK inline error message on the radios (no page-level error
        // summary component exists on this view), and stays on the same URL.
        await Expect(Page.Locator(".govuk-error-message")).ToBeVisibleAsync();
        await Expect(Page.Locator(".govuk-error-message")).ToContainTextAsync("Select what you would like to do");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"/CheckYourPupilData/{WindowId}"));
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
}
