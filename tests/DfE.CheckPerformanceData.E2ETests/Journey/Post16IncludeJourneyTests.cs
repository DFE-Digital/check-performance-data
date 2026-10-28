using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// #711 (AB#301154): the 16-19 Include journey on the seeded "16 to 19 Oct" window, as Kingsmead
// School (860/4070). Who can be picked is the INCLUDABLE stamp: every student in the non-included
// file, and the included file's P_INCL 502 students. The seed (SeedPupilData.GeneratePost16Pupils)
// gives the included file Alice Smith (501), Bob Smith (502), Charlie Smith (505) … and the
// non-included file Alice Johnson, Bob Johnson …
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class Post16IncludeJourneyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private static readonly Guid WindowId = Guid.Parse("EC6493B8-9B66-4090-8BE6-9DFC3805751F");

    private const string NonIncludedStudent = "Alice Johnson";   // non-included file
    private const string Code502Student = "Bob Smith";            // included file, P_INCL 502
    private const string Code501Student = "Alice Smith";          // included file, P_INCL 501
    private const string NoSuchName = "No Such";

    [RetryFact]
    [Trait("Category", "Smoke")]
    public async Task NonIncludedStudent_CanBeIncluded_ThroughToTheConfirmation()
    {
        // A leftover request for this student would divert the search into the conflict banner.
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await StartIncludeJourneyAsync();
        await SelectStudentAsync(NonIncludedStudent);

        // AC2/AC3: the evidence page, both answers optional — continue with nothing.
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/evidence");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync($"Provide evidence for the inclusion of {NonIncludedStudent}");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save and continue later" })).ToBeVisibleAsync();
        await ContinueAsync();

        // AC6: the summary names the student, the change and a Change link.
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Summary of amendment request");
        var summary = Page.Locator(".govuk-summary-list");
        await Expect(summary).ToContainTextAsync("Include a student in data");
        var nameRow = Page.Locator(".govuk-summary-list__row", new() { HasText = "Student name" });
        await Expect(nameRow).ToContainTextAsync(NonIncludedStudent);
        await Expect(nameRow.GetByRole(AriaRole.Link, new() { Name = "Change" })).ToBeVisibleAsync();

        // AC7/AC8: a reference number and the deadline for further amendments.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/confirmation");
        var panel = await Page.Locator(".govuk-panel").InnerTextAsync();
        Assert.Matches(@"CYPMD_16to19_[0-9A-F]{7}", panel);
        await Expect(Page.GetByText("to request any further amendments")).ToBeVisibleAsync();
    }

    [RetryFact]
    public async Task Code502Student_IsOffered_AndReachesTheEvidencePage()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await StartIncludeJourneyAsync();
        await SelectStudentAsync(Code502Student);

        // Not "This student is already included": the 502 student is includable by rule.
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/evidence");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync($"Provide evidence for the inclusion of {Code502Student}");
    }

    [RetryFact]
    public async Task Code501Student_IsNotOffered_AndTypingTheName_WarnsAlreadyIncluded_WithoutAdd()
    {
        await StartIncludeJourneyAsync();

        var input = Page.Locator("#pupil-search").First;
        await input.FillAsync(Code501Student);
        // AC9: a 501 student is not a suggestion. Every word of the query must match, so "Alice
        // Smith" can match nobody else; wait for the component's empty-state row rather than
        // counting options straight after typing (Ks4MergeJourneyTests.ExpectNoSuggestionsAsync).
        await Expect(Page.Locator(".autocomplete__option--no-results")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(Page.Locator("li[role='option']")).ToHaveCountAsync(0);
        await input.PressAsync("Escape");
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("This student is already included");
        Assert.Contains("/already-included", Page.Url);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Abort" })).ToBeVisibleAsync();
        // 16-19 has no Add journey, so no "Start adding" button.
        Assert.Equal(0, await Page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Start adding") }).CountAsync());
    }

    [RetryFact]
    public async Task UnknownName_ShowsStudentNotFound_WithSearchAgainOnly()
    {
        await StartIncludeJourneyAsync();

        var input = Page.Locator("#pupil-search").First;
        await input.FillAsync(NoSuchName);
        await Page.WaitForTimeoutAsync(250);
        await input.PressAsync("Escape");
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Student not found");
        Assert.Contains("/pupil-not-found", Page.Url);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Search again" })).ToBeVisibleAsync();
        Assert.Equal(0, await Page.GetByRole(AriaRole.Button, new() { NameRegex = new("^Start adding") }).CountAsync());

        await Page.GetByRole(AriaRole.Button, new() { Name = "Search again" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/pupil-search/select-pupil");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task StartIncludeJourneyAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{WindowId}");
        await Page.Locator("input[name='SelectedWhatToChange'][value='Include']").CheckAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/pupil-search/select-pupil");
    }

    // Types the name, picks the suggestion that carries it, and continues. The 16-19 suggestion
    // reads "Firstname, Surname, (CYPMD ID:…, ULN:…, DOB:…, INCLUDED|NOT INCLUDED)"
    // (PupilSuggestionFormat.Label); the rows are li[role='option'], as Ks4MergeJourneyTests picks them.
    private async Task SelectStudentAsync(string name)
    {
        var input = Page.Locator("#pupil-search").First;
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync(name);
        var parts = name.Split(' ');
        var option = Page.Locator("li[role='option']").GetByText($"{parts[0]}, {parts[1]},");
        await Expect(option.First).ToBeVisibleAsync();
        await option.First.ClickAsync();
        await ContinueAsync();
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
}
