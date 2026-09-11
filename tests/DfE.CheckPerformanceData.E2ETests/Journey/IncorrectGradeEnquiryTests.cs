using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#296648: the 16-19 "report an incorrect grade" journey, driven through a real browser.
//
// These cover what only a browser can: that the result and grade dropdowns post the value they show,
// that the journey holds together across every redirect, and that starting a second enquiry genuinely
// carries nothing over.
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

    // The same qualification in the previous session — the label differs only in the session, and
    // only the details (grade 4, not 5) tell the two apart. From SeedStudentResults.
    private const string BusStudsS2023 = "GCSE (9-1) Bus. Studs:Single, QAN: 6037116X, Session: S2023";

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
    public async Task TheCurrentGradeCannotBeChosenFromThePicker()
    {
        // AB#301913 / #407: the result's current grade is 5. It used to be offered (and refused on
        // POST); now the picker never lists it at all. The POST-side refusal of a forged "5" is
        // pinned at unit level (JourneyControllerResultDetailsTests.Post_the_current_grade_…).
        await NavigateToGradePageAsync();

        // The summary row still states the current grade, so the user knows what they are revising.
        // Scoped to the row: the same list holds the CYPMD ID 500001, which contains a "5" and would
        // satisfy a list-wide substring match even if this row vanished (review F3).
        var currentGradeRow = Page.Locator(".govuk-summary-list__row").Filter(new() { HasText = "Current grade" });
        await Expect(currentGradeRow).ToHaveCountAsync(1);
        await Expect(currentGradeRow.Locator(".govuk-summary-list__value")).ToHaveTextAsync(BusStudsCurrentGrade);

        // Positive count beside the negative one, so this fact cannot pass on an absent select:
        // placeholder + the ten remaining grades of the 9-1 scale (review F4).
        await Expect(Page.Locator("select[name='q_q_revised_grade'] option")).ToHaveCountAsync(11);
        var currentGradeOption = Page.Locator($"select[name='q_q_revised_grade'] option[value='{BusStudsCurrentGrade}']");
        await Expect(currentGradeOption).ToHaveCountAsync(0);
    }

    [RetryFact(3)]
    public async Task TheGradePickerOffersTheQualificationsOwnScaleMinusTheCurrentGrade()
    {
        // The GCSE 9-1 scale for this QAN, pass grades before fail grades, with a placeholder first
        // — and without "5", the grade the seeded S2024 result already holds (AB#301913).
        await NavigateToGradePageAsync();

        // By name rather than id, matching the other locators in this suite.
        var values = await Page.Locator("select[name='q_q_revised_grade'] option").EvaluateAllAsync<string[]>(
            "options => options.map(o => o.value)");

        Assert.Equal(["", "9", "8", "7", "6", "4", "3", "2", "1", "U", "X"], values);
    }

    [RetryFact(3)]
    public async Task AChosenGradeIsStillRestoredWhenTheUserComesBackToThePage()
    {
        // The AB#295434 restoration contract, on this picker: a Back to this page shows the grade
        // the user chose, not an empty field. The picker is a plain <select>, so restoration is the
        // server-side selected= attribute — there is no enhancement left to lose it.
        await NavigateToGradePageAsync();
        await SelectGradeAsync("4");
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/additional-info");

        await Page.Locator("a.govuk-back-link").ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/grade-details");

        await Expect(Page.Locator("select[name='q_q_revised_grade']")).ToHaveValueAsync("4");
    }

    // ── AB#301934 / #409: a chosen result's details appear as soon as it is picked ──────────

    // The details a chosen result reveals. All six rows are asserted — the CSV file row is the
    // one thing the option label cannot show, and the grade is what separates the two sessions.
    private static ILocator ShownDetails(IPage page) =>
        page.Locator("form .govuk-summary-list:visible");

    private async Task AssertDetailsRowAsync(ILocator details, string key, string value)
    {
        var row = details.Locator(".govuk-summary-list__row", new() { HasText = key });
        await Expect(row.Locator(".govuk-summary-list__value")).ToHaveTextAsync(value);
    }

    private async Task NavigateToResultSearchAsync()
    {
        await StartEnquiryAsync();
        await ContinueAsync();
        await ChooseCohortScopeAsync("no");
        await ChooseStudentAsync("select-student-single");
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/result-search/select-result");
    }

    [RetryFact(3)]
    public async Task ChoosingAResultShowsItsDetailsWithoutLeavingThePage()
    {
        // #409: the details used to render only from the session, i.e. only after the user had
        // continued past this page and come back. Picking a suggestion must reveal them here.
        await NavigateToResultSearchAsync();
        var details = ShownDetails(Page);
        await Expect(details).ToHaveCountAsync(0);

        await PickResultAsync(BusStudsS2024);

        await Expect(details).ToHaveCountAsync(1);
        await AssertDetailsRowAsync(details, "Qualification name and subject", "GCSE (9-1) Bus. Studs:Single");
        await AssertDetailsRowAsync(details, "Qualification number (QAN)", "6037116X");
        await AssertDetailsRowAsync(details, "Syllabus code", "1BS0");
        await AssertDetailsRowAsync(details, "Session", "S2024");
        await AssertDetailsRowAsync(details, "Current Grade", BusStudsCurrentGrade);
        await AssertDetailsRowAsync(details, "CSV file", "16to19_MAIN");
        // No round-trip: still on the search page, nothing posted.
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(
            $"/Journey/{WindowId}/result-search/select-result$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        // Picking the other session swaps the details — the label alone cannot show the grade.
        await PickResultAsync(BusStudsS2023);
        await Expect(details).ToHaveCountAsync(1);
        await AssertDetailsRowAsync(details, "Session", "S2023");
        await AssertDetailsRowAsync(details, "Current Grade", "4");
        await Expect(Page.Locator("select[name='selectedResultKey']")).ToHaveValueAsync("6037116X|S2023|16to19_MAIN");
    }

    [RetryFact(3)]
    public async Task ClearingTheChoiceHidesTheDetailsUntilAResultIsPickedAgain()
    {
        // Going back to the empty placeholder option must take the details with it — the page must
        // not keep describing a result the control no longer holds — and they must come back on the
        // next pick.
        await NavigateToResultSearchAsync();
        var details = ShownDetails(Page);
        await PickResultAsync(BusStudsS2024);
        await Expect(details).ToHaveCountAsync(1);

        await Page.Locator("select[name='selectedResultKey']").SelectOptionAsync(string.Empty);
        await Expect(details).ToHaveCountAsync(0);

        await PickResultAsync(BusStudsS2024);
        await Expect(details).ToHaveCountAsync(1);
        await AssertDetailsRowAsync(details, "Session", "S2024");
    }

    [RetryFact(3)]
    public async Task TheChosenResultsDetailsAreStillShownWhenTheUserComesBackToThePage()
    {
        // The guard for the fix: the server-side render of a result the session already holds is
        // the path that worked before #409 and must keep working with no script involved — the
        // field shows the label and exactly one details block is visible, with the right session.
        await NavigateToResultSearchAsync();
        await ChooseResultAsync(BusStudsS2024);
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/grade-details");

        await Page.Locator("a.govuk-back-link").ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/result-search/select-result");

        await Expect(Page.Locator("select[name='selectedResultKey']"))
            .ToHaveValueAsync("6037116X|S2024|16to19_MAIN");
        var details = ShownDetails(Page);
        await Expect(details).ToHaveCountAsync(1);
        await AssertDetailsRowAsync(details, "Session", "S2024");
        await AssertDetailsRowAsync(details, "Current Grade", BusStudsCurrentGrade);
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
    /// Picks a result from the plain server-rendered &lt;select&gt;. The type-ahead this control
    /// once carried is gone — a student holds a handful of results, so the list is read.
    /// </summary>
    private async Task ChooseResultAsync(string label)
    {
        await PickResultAsync(label);
        await ContinueAsync();
    }

    /// <summary>
    /// Picks a result without continuing, so a test can look at what the page does in response
    /// (AB#301934 — the details block the choice reveals).
    /// </summary>
    private async Task PickResultAsync(string label)
    {
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/result-search/select-result");
        var select = Page.Locator("select[name='selectedResultKey']");
        await Expect(select).ToBeVisibleAsync();
        await select.SelectOptionAsync(new SelectOptionValue { Label = label });
        await Expect(select.Locator("option:checked")).ToContainTextAsync(label);
    }

    private async Task ChooseRevisedGradeAsync(string grade)
    {
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/grade-details");
        await SelectGradeAsync(grade);
        await ContinueAsync();
    }

    // The grade picker is a plain server-rendered <select> with no JavaScript enhancement — the
    // scale is short enough to read, so the option is chosen on the select itself.
    private async Task SelectGradeAsync(string grade)
    {
        var select = Page.Locator("select[name='q_q_revised_grade']");
        await Expect(select).ToBeVisibleAsync();
        await select.SelectOptionAsync(grade);
        await Expect(select).ToHaveValueAsync(grade);
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
