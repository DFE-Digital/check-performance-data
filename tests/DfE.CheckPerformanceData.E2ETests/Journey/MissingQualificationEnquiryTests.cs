using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
// xRetry's RetryFact is only needed as the [RetryFact] attribute here; importing `using xRetry;`
// would also drag in an Xunit.Skip that collides with SkippableFact's Xunit.Skip (used in the
// Cancelling test to skip explicitly when the ChangeRequests table is unreachable), so alias just
// the attribute.
using RetryFact = xRetry.RetryFactAttribute;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#297848: the 16-19 "missing qualification" journey — the sibling to IncorrectGradeEnquiryTests.
//
// These cover what only a browser can: that the qualification-search page's plain <select>s (AO,
// then QAN grouped by AO) genuinely work without further help, that the details page's syllabus
// picker accessible-autocomplete enhancement activates just like the grade picker's, and that the
// whole journey holds together end to end with no late-results interstitial in the way.
[Collection("E2E")]
public sealed class MissingQualificationEnquiryTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded Post16 window (DevDataSeeder.Post16CheckingWindowId) — same window and student the
    // incorrect-grade E2E suite uses.
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    private const string StudentCypmdId = "500001";
    private const string StudentName = "Alice Smith";

    // AQA GCSE (9-1) Mathematics — one of the 13 QANs the SyllabusCodes export covers for 16-19.
    private const string Qan = "60146084";
    private const string AwardingOrganisation = "AQA";
    private const string SyllabusCode = "8300H";
    private const string SyllabusLabel = "8300H — Mathematics Higher Tier";

    [RetryFact(3)]
    public async Task A_school_can_report_a_missing_qualification_end_to_end()
    {
        await StartEnquiryAsync();

        await ChooseCohortScopeAsync("no");
        await ChooseStudentAsync("select-student-single");
        await ChooseQualificationAsync();
        await FillDetailsAsync(syllabus: SyllabusCode, day: "1", month: "6", year: "2025", grade: "9", ncn: "12345");
        await FillAdditionalInfoAsync(string.Empty);

        // Check answers.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync($"Summary of result enquiry for {StudentName}");
        var summary = await Page.Locator(".govuk-summary-list").InnerTextAsync();
        Assert.Contains("Missing qualification", summary);
        Assert.Contains(AwardingOrganisation, summary);
        Assert.Contains(Qan, summary);
        Assert.Contains(SyllabusCode, summary);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();

        await Expect(Page.Locator(".govuk-panel")).ToContainTextAsync("Results enquiry submitted");
        var reference = await ReadReferenceAsync();
        Assert.Matches(@"^CYPMD_16to19_RE_[0-9A-F]{7}$", reference);
    }

    [RetryFact(3)]
    public async Task An_award_date_before_september_2023_is_rejected_with_the_window_message()
    {
        await StartEnquiryAsync();
        await ChooseCohortScopeAsync("no");
        await ChooseStudentAsync("select-student-single");
        await ChooseQualificationAsync();

        await SelectSyllabusAsync(SyllabusCode);
        await FillDateAsync("q-award-date", "1", "8", "2023");
        await SelectMissingGradeAsync("9");
        await ContinueAsync();

        await AssertErrorAsync(
            "We are only able to allow results enquiries for results awarded during the "
            + "2023/24 and 2024/25 academic years");
    }

    [SkippableFact]
    public async Task Cancelling_from_the_summary_discards_the_enquiry_and_starts_fresh()
    {
        // AB#298229. Three ACs in one walk: nothing submitted, no data carried over, and the
        // chooser has no option pre-selected. The deep-link check at the end is the load-bearing
        // one — before this ticket, Cancel's target cleared nothing, so the "cancelled" enquiry
        // was still sitting in session, one summary URL away from being submitted.
        // AC: "nothing I entered is submitted" — proven at the table, not inferred from the UI
        // (an enquiry row would be invisible on every school-facing list by design). Local
        // workflows reach the compose Postgres and run it for real; in CI there is no reachable
        // Postgres, so the probe is unavailable and this AC is reported as Skipped, not silently
        // passed — see ChangeRequestProbeHelper.
        var rowsBefore = await ChangeRequestProbeHelper.TryCountChangeRequestsAsync();
        Skip.IfNot(rowsBefore is not null, ChangeRequestProbeHelper.UnavailableReason);

        await StartEnquiryAsync();
        await ChooseCohortScopeAsync("no");
        await ChooseStudentAsync("select-student-single");
        await ChooseQualificationAsync();
        await FillDetailsAsync(syllabus: SyllabusCode, day: "1", month: "6", year: "2025", grade: "9", ncn: "12345");
        await FillAdditionalInfoAsync(string.Empty);

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync($"Summary of result enquiry for {StudentName}");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Cancel and go back to create a new enquiry" })
            .ClickAsync();

        // Lands on the enquiry-type chooser…
        await Page.WaitForURLAsync($"**/{WindowId}/ResultIssue");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("What issue with the results do you need to report?");

        // …with nothing pre-selected.
        Assert.Equal(0, await Page.Locator("input[name='IssueType']:checked").CountAsync());

        // And the abandoned enquiry is unreachable: deep-linking back to the summary bounces out
        // of the journey (IsSessionReady fails on the cleared state) instead of re-rendering the
        // cancelled answers.
        await Page.GotoAsync($"{Fixture.BaseUrl}/Journey/{WindowId}/summary");
        await Page.WaitForURLAsync($"**/CheckYourPupilData/{WindowId}");

        await ChangeRequestProbeHelper.AssertNoRowsCreatedBetweenAsync(rowsBefore);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task StartEnquiryAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/{WindowId}/ResultIssue");
        await Page.Locator("input[name='IssueType'][value='missing-qualification']")
            .CheckAsync(new() { Force = true });
        await ContinueAsync();
        // No late-results interstitial on this journey — straight to cohort-scope.
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/cohort-scope");
    }

    private async Task ChooseCohortScopeAsync(string value)
    {
        await Page.Locator($"input[name='q_q_cohort_scope'][value='{value}']").CheckAsync(new() { Force = true });
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
    /// The AO then QAN pickers are plain, no-JS-required &lt;select&gt;s (unlike the accessible
    /// -autocomplete pupil/result/syllabus/grade controls) — every QAN renders grouped by AO, and a
    /// script narrows the visible group to the chosen AO. Selecting AO first keeps the QAN's
    /// optgroup enabled for the second select.
    /// </summary>
    private async Task ChooseQualificationAsync()
    {
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/qualification-search/select-qualification");
        await Page.Locator("#selectedAo").SelectOptionAsync(AwardingOrganisation);
        await Page.Locator("#selectedQan").SelectOptionAsync(Qan);
        await ContinueAsync();
    }

    private async Task FillDetailsAsync(string syllabus, string day, string month, string year, string grade, string ncn)
    {
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/qualification-details");
        await SelectSyllabusAsync(syllabus);
        await FillDateAsync("q-award-date", day, month, year);
        await SelectMissingGradeAsync(grade);
        if (ncn.Length > 0)
            await Page.Locator("#q_q_ncn").FillAsync(ncn);
        await ContinueAsync();
    }

    // The syllabus picker is a server-rendered <select> that accessible-autocomplete upgrades in
    // place, exactly like the grade picker — driving the enhanced input is the point, proving the
    // enhancement actually activates.
    private async Task SelectSyllabusAsync(string code)
    {
        var input = Page.Locator("input#q_q_syllabus_code");
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync(code);

        var option = Page.Locator("#q_q_syllabus_code__listbox li[role='option']")
            .GetByText(code, new() { Exact = false });
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();

        await Expect(Page.Locator("select[name='q_q_syllabus_code']")).ToHaveValueAsync(code);
    }

    private async Task SelectMissingGradeAsync(string grade)
    {
        var input = Page.Locator("input#q_q_missing_grade");
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync(grade);

        var option = Page.Locator("#q_q_missing_grade__listbox li[role='option']")
            .GetByText(grade, new() { Exact = true });
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();

        await Expect(Page.Locator("select[name='q_q_missing_grade']")).ToHaveValueAsync(grade);
    }

    // QuestionPartialModel renders date inputs as q_<id>_day/_month/_year, where the question id's
    // dashes become underscores.
    private async Task FillDateAsync(string questionId, string day, string month, string year)
    {
        var baseId = $"q_{questionId.Replace("-", "_")}";
        await Page.Locator($"#{baseId}_day").FillAsync(day);
        await Page.Locator($"#{baseId}_month").FillAsync(month);
        await Page.Locator($"#{baseId}_year").FillAsync(year);
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
