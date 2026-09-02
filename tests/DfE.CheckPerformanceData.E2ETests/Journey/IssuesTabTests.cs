using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#298325: the Issues tab on the Amendment request summary page. Covers what only a browser
// can: that a just-submitted enquiry appears in the tab with its blob-enriched cells, that the
// GET search round-trips AND re-selects the Issues tab (the #issues fragment doing its job), and
// that enquiries never leak into the Requests tab.
[Collection("E2E")]
public sealed class IssuesTabTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");
    private const string StudentCypmdId = "500001";
    private const string StudentName = "Alice Smith";
    private const string BusStudsS2024 = "GCSE (9-1) Bus. Studs:Single, QAN: 6037116X, Session: S2024";

    [RetryFact(3)]
    public async Task ASubmittedEnquiryAppearsOnTheIssuesTabAndIsSearchable()
    {
        // Submit a real enquiry through the journey so the row AND its journey blob exist.
        await StartEnquiryAsync();
        await ChooseStudentAsync();
        await ChooseResultAsync(BusStudsS2024);
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/additional-info");
        await ContinueAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();
        await ReadReferenceAsync();

        await Page.GotoAsync($"{Fixture.BaseUrl}/{WindowId}/AmendmentRequests");
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Issues" }).ClickAsync();

        var issuesPanel = Page.Locator("#issues");
        await Expect(issuesPanel.GetByText(StudentName).First).ToBeVisibleAsync();
        await Expect(issuesPanel.GetByText(StudentCypmdId).First).ToBeVisibleAsync();
        await Expect(issuesPanel.GetByText("Result does not belong to student").First).ToBeVisibleAsync();
        await Expect(issuesPanel.GetByText("GCSE (9-1) Bus. Studs:Single").First).ToBeVisibleAsync();

        // Search round trip: the term filters and the user LANDS back on the Issues tab — the
        // whole point of the #issues fragment. Asserting only the response would miss a bounce
        // back to the Requests tab.
        await issuesPanel.Locator("#issue-search").FillAsync("alice");
        await issuesPanel.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        await Page.WaitForURLAsync($"**/{WindowId}/AmendmentRequests?issueSearch=alice#issues");
        await Expect(Page.Locator("#issues").GetByText(StudentName).First).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Issues" }))
            .ToHaveAttributeAsync("aria-selected", "true");

        // A search that matches nothing keeps the search visible and says so, without the
        // "there are none" empty state (issues DO exist).
        await Page.Locator("#issues #issue-search").FillAsync("zzzznotaname");
        await Page.Locator("#issues").GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        await Expect(Page.Locator("#issues")
            .GetByText("There are no submitted result enquiries matching your search")).ToBeVisibleAsync();

        // Separation (AC 4): the enquiry never leaks into the Requests tab.
        await Page.GetByRole(AriaRole.Tab, new() { Name = "Requests" }).ClickAsync();
        Assert.Equal(0, await Page.Locator("#requests").GetByText("Result does not belong to student").CountAsync());
    }

    // ── helpers (copied from ResultDoesNotBelongEnquiryTests) ──────────────────

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
}
