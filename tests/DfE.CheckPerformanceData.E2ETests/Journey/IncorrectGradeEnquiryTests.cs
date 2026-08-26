using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#296648: the 16-19 "report an incorrect grade" journey, driven through a real browser.
//
// These cover what only a browser can: that the accessible-autocomplete enhancement of the result and
// grade pickers actually works (both are server-rendered <select>s upgraded in place), that the
// journey holds together across every redirect, and that starting a second enquiry genuinely carries
// nothing over.
//
// The seed (SeedStudentResults) deliberately writes no 16to19_LR2 rows, so the late-results
// interstitial is on the happy path locally.
[Collection("E2E")]
public sealed class IncorrectGradeEnquiryTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded Post16 window (DevDataSeeder.Post16CheckingWindowId).
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    // Kingsmead's first seeded Post16 student, and the results SeedStudentResults gives them.
    private const string StudentCypmdId = "500001";
    private const string StudentName = "Alice Smith";
    private const string BusStudsS2024 = "GCSE (9-1) Bus. Studs:Single, QAN: 6037116X, Session: S2024";
    private const string BusStudsCurrentGrade = "5";

    // ── The cohort-wide happy path, end to end ───────────────────────────────

    [RetryFact(3)]
    public async Task CohortWide_HappyPath_SubmitsAndShowsAReference()
    {
        await StartEnquiryAsync();

        // The interstitial is shown because the seed holds no second-late-results rows.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync(
            "Check your second late results file before you report an incorrect grade");
        await ContinueAsync();

        await ChooseCohortScopeAsync("yes");
        await FillCohortCountAsync("10");
        await ChooseStudentAsync("select-student-cohort");
        await ChooseResultAsync(BusStudsS2024);
        await ChooseRevisedGradeAsync("1");
        await FillAdditionalInfoAsync("The whole class was marked against the wrong paper.");

        // Check answers.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync($"Summary of result enquiry for {StudentName}");
        var summary = await Page.Locator(".govuk-summary-list").InnerTextAsync();
        Assert.Contains("Number of students in affected cohort", summary);
        Assert.Contains("Name of a student in cohort", summary);
        Assert.Contains("6037116X", summary);
        Assert.Contains("Incorrect grade", summary);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();

        await Expect(Page.Locator(".govuk-panel")).ToContainTextAsync("Results enquiry submitted");
        var reference = await ReadReferenceAsync();
        Assert.Matches(@"^CYPMD_16to19_RE_[0-9A-F]{7}$", reference);
    }

    // ── The single-student branch ────────────────────────────────────────────

    [RetryFact(3)]
    public async Task SingleStudent_Branch_AsksForOneStudentAndSubmits()
    {
        await StartEnquiryAsync();
        await ContinueAsync();

        await ChooseCohortScopeAsync("no");

        // No count page on this branch — straight to the single-student page, with its own heading.
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/pupil-search/select-student-single");
        await Expect(Page.Locator("label[for='pupil-search']"))
            .ToContainTextAsync("What is the name of the student with an incorrect grade?");

        await ChooseStudentAsync("select-student-single");
        await ChooseResultAsync(BusStudsS2024);
        await ChooseRevisedGradeAsync("2");
        await FillAdditionalInfoAsync(string.Empty);

        var summary = await Page.Locator(".govuk-summary-list").InnerTextAsync();
        Assert.Contains("Name of student", summary);
        Assert.DoesNotContain("Number of students in affected cohort", summary);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();
        Assert.Matches(@"^CYPMD_16to19_RE_[0-9A-F]{7}$", await ReadReferenceAsync());
    }

    // ── Nothing carries over into the next enquiry ───────────────────────────

    [RetryFact(3)]
    public async Task AfterSubmitting_ReportingAnotherIssue_StartsClean()
    {
        await StartEnquiryAsync();
        await ContinueAsync();
        await ChooseCohortScopeAsync("yes");
        await FillCohortCountAsync("10");
        await ChooseStudentAsync("select-student-cohort");
        await ChooseResultAsync(BusStudsS2024);
        await ChooseRevisedGradeAsync("3");
        await FillAdditionalInfoAsync(string.Empty);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();
        var first = await ReadReferenceAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Report another issue with an exam result" })
            .ClickAsync();

        // The issue page must be blank: nothing selected.
        await Page.WaitForURLAsync($"**/{WindowId}/ResultIssue");
        await Expect(Page.Locator("#issueType")).Not.ToBeCheckedAsync();

        // And continuing in must land on a cohort question with nothing selected either.
        await Page.Locator("#issueType").CheckAsync(new() { Force = true });
        await ContinueAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/cohort-scope");
        Assert.Empty(await Page.Locator("input[name='q_q_cohort_scope']:checked").AllAsync());

        // A second enquiry for the same student and result is allowed, with its own reference.
        await ChooseCohortScopeAsync("no");
        await ChooseStudentAsync("select-student-single");
        await ChooseResultAsync(BusStudsS2024);
        await ChooseRevisedGradeAsync("4");
        await FillAdditionalInfoAsync(string.Empty);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();

        var second = await ReadReferenceAsync();
        Assert.Matches(@"^CYPMD_16to19_RE_[0-9A-F]{7}$", second);
        Assert.NotEqual(first, second);
    }

    // ── Error states ────────────────────────────────────────────────────────

    [RetryFact(3)]
    public async Task ContinuingWithoutChoosingAnIssue_ShowsTheError()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/{WindowId}/ResultIssue");
        await ContinueAsync();

        await AssertErrorAsync("Select what issue with the results you need to report");
    }

    [RetryFact(3)]
    public async Task ContinuingWithoutAnsweringTheCohortQuestion_ShowsTheError()
    {
        await StartEnquiryAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/cohort-scope");

        await ContinueAsync();

        await AssertErrorAsync("Select if the incorrect grade affects the whole cohort");
    }

    [RetryFact(3)]
    public async Task ANonNumericCohortCount_ShowsTheError()
    {
        await StartEnquiryAsync();
        await ContinueAsync();
        await ChooseCohortScopeAsync("yes");

        await Page.Locator("#q_q_cohort_count").FillAsync("ten");
        await ContinueAsync();

        await AssertErrorAsync("Enter how many students have an incorrect grade for this qualification");
    }

    [RetryFact(3)]
    public async Task ContinuingWithoutChoosingAResult_ShowsTheTemplatedError()
    {
        await StartEnquiryAsync();
        await ContinueAsync();
        await ChooseCohortScopeAsync("no");
        await ChooseStudentAsync("select-student-single");

        await ContinueAsync();

        await AssertErrorAsync($"Enter which of {StudentName}'s results is incorrect");
    }

    [RetryFact(3)]
    public async Task ContinuingWithoutChoosingAGrade_ShowsTheError()
    {
        await NavigateToGradePageAsync();

        await ContinueAsync();

        await AssertErrorAsync("Select the revised grade");
    }

    [RetryFact(3)]
    public async Task ChoosingTheCurrentGrade_ShowsTheMustDifferError()
    {
        // The result's current grade is 5, so choosing 5 reports no change.
        await NavigateToGradePageAsync();

        await SelectGradeAsync(BusStudsCurrentGrade);
        await ContinueAsync();

        await AssertErrorAsync("The revised grade must be different from the current grade");
    }

    [RetryFact(3)]
    public async Task TheGradePickerOffersTheQualificationsOwnScale()
    {
        // The GCSE 9-1 scale for this QAN, pass grades before fail grades, with a placeholder first.
        await NavigateToGradePageAsync();

        // By name, not id: enhancement renames the select's id to "-select" but keeps its name,
        // so this locator finds the full server-rendered scale whether or not the script has run.
        var values = await Page.Locator("select[name='q_q_revised_grade'] option").EvaluateAllAsync<string[]>(
            "options => options.map(o => o.value)");

        Assert.Equal(["", "9", "8", "7", "6", "5", "4", "3", "2", "1", "U", "X"], values);
    }

    // ── The way in, and the auth gate ───────────────────────────────────────

    [RetryFact(3)]
    public async Task TheCheckYourStudentDataPageOffersTheEnquiryOption()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/CheckYourPupilData/{WindowId}");

        var option = Page.Locator("input[name='SelectedNextStep'][value='ResultsEnquiry']");
        await Expect(option).ToBeAttachedAsync();

        await option.CheckAsync(new() { Force = true });
        await ContinueAsync();

        await Page.WaitForURLAsync($"**/{WindowId}/ResultIssue");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("What issue with the results do you need to report?");
    }

    [RetryFact(3)]
    public async Task AnAnonymousRequestForTheIssuePageIsSentToSignIn()
    {
        // TestHttpClients.NoRedirect has UseCookies=false and no impersonation cookie attached, so
        // this request is genuinely anonymous.
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{Fixture.BaseUrl}/{WindowId}/ResultIssue");

        var response = await TestHttpClients.NoRedirect.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("signin.education.gov.uk", response.Headers.Location?.ToString() ?? string.Empty);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task StartEnquiryAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/{WindowId}/ResultIssue");
        await Page.Locator("#issueType").CheckAsync(new() { Force = true });
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/check-late-results");
    }

    private async Task NavigateToGradePageAsync()
    {
        await StartEnquiryAsync();
        await ContinueAsync();
        await ChooseCohortScopeAsync("no");
        await ChooseStudentAsync("select-student-single");
        await ChooseResultAsync(BusStudsS2024);
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/grade-details");
    }

    private async Task ChooseCohortScopeAsync(string value)
    {
        // By option value, not label, so a copy edit cannot break the test.
        await Page.Locator($"input[name='q_q_cohort_scope'][value='{value}']").CheckAsync(new() { Force = true });
        await ContinueAsync();
    }

    private async Task FillCohortCountAsync(string count)
    {
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/cohort-count");
        await Page.Locator("#q_q_cohort_count").FillAsync(count);
        await ContinueAsync();
    }

    private async Task ChooseStudentAsync(string pageId)
    {
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/pupil-search/{pageId}");
        var search = Page.Locator("#pupil-search").First;
        await Expect(search).ToBeVisibleAsync();
        await search.FillAsync(StudentCypmdId);
        var option = Page.Locator("li[role='option']").GetByText(StudentCypmdId);
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();
        await ContinueAsync();
    }

    /// <summary>
    /// Picks a result through the enhanced autocomplete. The control is a server-rendered
    /// &lt;select&gt; that accessible-autocomplete upgrades in place, so exercising it here is what
    /// proves the enhancement works — a unit test can only see the select.
    /// </summary>
    private async Task ChooseResultAsync(string label)
    {
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/result-search/select-result");
        var search = Page.Locator("#result-search").First;
        await Expect(search).ToBeVisibleAsync();
        await search.FillAsync("Bus");
        var option = Page.Locator("li[role='option']").GetByText(label, new() { Exact = false });
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();

        // The autocomplete updates the hidden select asynchronously. Wait for the value that the
        // form actually posts before submitting, otherwise a fast click on Continue can redisplay
        // this page with the no-selection validation error.
        await Expect(Page.Locator("select[name='selectedResultKey'] option:checked"))
            .ToContainTextAsync(label);

        // The open autocomplete menu can consume Playwright's pointer click on Continue without
        // submitting the form. Native requestSubmit preserves browser validation and submit events.
        await Page.Locator("form").EvaluateAsync("form => form.requestSubmit()");
    }

    private async Task ChooseRevisedGradeAsync(string grade)
    {
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/grade-details");
        await SelectGradeAsync(grade);
        await ContinueAsync();
    }

    // The grade picker is a server-rendered <select> that accessible-autocomplete upgrades in
    // place: the select is renamed "#q_q_revised_grade-select" and hidden, and the visible control
    // becomes an <input> that takes the select's original id. Driving the input is the point — it
    // proves the enhancement works, exactly like ChooseStudentAsync and ChooseResultAsync do for
    // the other two autocompletes. There is deliberately no fallback to the raw select: if the
    // enhancement stops activating, these tests must fail, not quietly route around it.
    private async Task SelectGradeAsync(string grade)
    {
        var input = Page.Locator("input#q_q_revised_grade");
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync(grade);

        var option = Page.Locator("#q_q_revised_grade__listbox li[role='option']")
            .GetByText(grade, new() { Exact = true });
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();

        // Confirming the option writes the value into the hidden select, which is what the form
        // posts — waiting on it here makes the subsequent Continue deterministic.
        await Expect(Page.Locator("select[name='q_q_revised_grade']")).ToHaveValueAsync(grade);
    }

    private async Task FillAdditionalInfoAsync(string text)
    {
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/additional-info");
        if (text.Length > 0)
            await Page.Locator("#q_q_additional_info").FillAsync(text);
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/summary");
    }

    private async Task<string> ReadReferenceAsync()
    {
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/enquiry-confirmation");
        var panel = await Page.Locator(".govuk-panel").InnerTextAsync();
        var match = System.Text.RegularExpressions.Regex.Match(panel, @"CYPMD_16to19_RE_[0-9A-F]{7}");
        Assert.True(match.Success, $"No reference number in the confirmation panel: {panel}");
        return match.Value;
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

    private async Task AssertErrorAsync(string expected)
    {
        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        var text = await Page.Locator(".govuk-error-summary").InnerTextAsync();
        Assert.Contains(expected, text);
    }
}
