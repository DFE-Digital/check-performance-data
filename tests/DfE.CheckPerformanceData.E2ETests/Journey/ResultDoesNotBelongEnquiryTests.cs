using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Npgsql;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#298704: the 16-19 "result does not belong to student" journey — the third sibling to
// IncorrectGradeEnquiryTests and MissingQualificationEnquiryTests.
//
// These cover what only a browser can: that the journey holds together across every redirect with
// no cohort question and no late-results interstitial in the way, that the result page's inset text
// and the additional-information page's 1,000-character limit render, and that the summary omits the
// revised-grade row entirely rather than showing it empty.
[Collection("E2E")]
public sealed class ResultDoesNotBelongEnquiryTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded Post16 window (DevDataSeeder.Post16CheckingWindowId) — same window and student the
    // sibling enquiry suites use.
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");

    private const string StudentCypmdId = "500001";
    private const string StudentName = "Alice Smith";
    private const string BusStudsS2024 = "GCSE (9-1) Bus. Studs:Single, QAN: 6037116X, Session: S2024";

    [RetryFact(3)]
    public async Task A_school_can_report_a_result_that_does_not_belong_end_to_end()
    {
        await StartEnquiryAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("What is the name of the student the result does not belong to?");
        await ChooseStudentAsync();

        await Expect(Page.Locator(".govuk-inset-text")).ToContainTextAsync(
            "If more than one result does not belong to the student, provide the QAN and grade for "
            + "each result on the next page.");
        await ChooseResultAsync(BusStudsS2024);

        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/additional-info");
        await Expect(Page.Locator(".govuk-character-count")).ToHaveAttributeAsync("data-maxlength", "1000");
        // The govuk-character-count JS enhancement swaps the static "You can enter up to 1000
        // characters" hint for a live count — Figma f06's exact "You have 1,000 characters
        // remaining" — proving the enhancement actually activates, not just that markup is present.
        await Expect(Page.Locator(".govuk-character-count__status"))
            .ToContainTextAsync("You have 1,000 characters remaining");
        await Page.Locator("#q_q_additional_info").FillAsync("QAN 60322222 grade B also does not belong");
        await ContinueAsync();

        // Check answers.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync($"Summary of result enquiry for {StudentName}");
        var summary = await Page.Locator(".govuk-summary-list").InnerTextAsync();
        Assert.Contains("Result does not belong to student", summary);
        Assert.Contains("6037116X", summary);
        Assert.DoesNotContain("Revised grade", summary);
        Assert.Equal(1, await Page.Locator(".govuk-summary-list__actions a", new() { HasTextString = "Change" }).CountAsync());

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();

        await Expect(Page.Locator(".govuk-panel")).ToContainTextAsync("Results enquiry submitted");
        var reference = await ReadReferenceAsync();
        Assert.Matches(@"^CYPMD_16to19_RE_[0-9A-F]{7}$", reference);
    }

    [RetryFact(3)]
    public async Task Additional_information_can_be_left_blank()
    {
        // AC: "proceed without entering any" — the row stays but is shown empty.
        await StartEnquiryAsync();
        await ChooseStudentAsync();
        await ChooseResultAsync(BusStudsS2024);

        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/additional-info");
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync($"Summary of result enquiry for {StudentName}");
        var row = Page.Locator(".govuk-summary-list__row", new() { HasText = "Additional information" });
        await Expect(row.Locator(".govuk-summary-list__value")).ToHaveTextAsync("");
    }

    [RetryFact(3)]
    public async Task MoreThanOneThousandCharactersIsRejectedOnAForcedPost()
    {
        // The JS character-count component only warns; nothing native stops a script-driven POST
        // from carrying more than the limit, so the server must still reject it.
        await StartEnquiryAsync();
        await ChooseStudentAsync();
        await ChooseResultAsync(BusStudsS2024);

        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/additional-info");
        await Page.Locator("#q_q_additional_info").FillAsync(new string('a', 1001));
        await Page.Locator("form").EvaluateAsync("form => form.requestSubmit()");

        await AssertErrorAsync("Additional information must be 1000 characters or less");
    }

    [RetryFact(3)]
    public async Task AfterSubmitting_ReportingAnotherIssue_StartsClean()
    {
        await StartEnquiryAsync();
        await ChooseStudentAsync();
        await ChooseResultAsync(BusStudsS2024);
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/additional-info");
        await ContinueAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();
        var first = await ReadReferenceAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Report another issue with an exam result" })
            .ClickAsync();

        await Page.WaitForURLAsync($"**/{WindowId}/ResultIssue");
        Assert.Equal(0, await Page.Locator("input[name='IssueType']:checked").CountAsync());

        await StartEnquiryAsync();
        await ChooseStudentAsync();
        await ChooseResultAsync(BusStudsS2024);
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/additional-info");
        await ContinueAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();

        var second = await ReadReferenceAsync();
        Assert.NotEqual(first, second);
    }

    [RetryFact(3)]
    public async Task Cancelling_from_the_summary_discards_the_enquiry_and_starts_fresh()
    {
        var rowsBefore = await CountChangeRequestsAsync();

        await StartEnquiryAsync();
        await ChooseStudentAsync();
        await ChooseResultAsync(BusStudsS2024);
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/additional-info");
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync($"Summary of result enquiry for {StudentName}");

        await Page.GetByRole(AriaRole.Link, new() { Name = "Cancel and go back to create a new enquiry" })
            .ClickAsync();

        await Page.WaitForURLAsync($"**/{WindowId}/ResultIssue");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("What issue with the results do you need to report?");
        Assert.Equal(0, await Page.Locator("input[name='IssueType']:checked").CountAsync());

        await Page.GotoAsync($"{Fixture.BaseUrl}/Journey/{WindowId}/summary");
        await Page.WaitForURLAsync($"**/CheckYourPupilData/{WindowId}");

        Assert.Equal(rowsBefore, await CountChangeRequestsAsync());
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private async Task StartEnquiryAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/{WindowId}/ResultIssue");
        await Page.Locator("input[name='IssueType'][value='result-does-not-belong']")
            .CheckAsync(new() { Force = true });
        await ContinueAsync();
        // No cohort question and no late-results interstitial — straight to the student search.
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/pupil-search/select-student");
    }

    private async Task ChooseStudentAsync()
    {
        var search = Page.Locator("#pupil-search").First;
        await Expect(search).ToBeVisibleAsync();
        await search.FillAsync(StudentCypmdId);
        var option = Page.Locator("li[role='option']").GetByText(StudentCypmdId);
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();
        await ContinueAsync();
    }

    /// <summary>
    /// Picks a result through the enhanced autocomplete, mirroring
    /// IncorrectGradeEnquiryTests.ChooseResultAsync — the control is a server-rendered
    /// &lt;select&gt; that accessible-autocomplete upgrades in place.
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

        await Expect(Page.Locator("select[name='selectedResultKey'] option:checked"))
            .ToContainTextAsync(label);

        await Page.Locator("form").EvaluateAsync("form => form.requestSubmit()");
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

    private static async Task<long> CountChangeRequestsAsync()
    {
        var cs = Environment.GetEnvironmentVariable("CPD_E2E_DB")
            ?? "Host=localhost;Port=5432;Database=cypd;Username=postgres;Password=postgres";
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM \"ChangeRequests\"", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
