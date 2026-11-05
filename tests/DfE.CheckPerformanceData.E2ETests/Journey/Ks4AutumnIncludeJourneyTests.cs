using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// Issue #750 / AB#312009: the KS4 Autumn "Include a pupil" journey. It is the KS4 June include
// journey. Who can be picked is the INCLUDABLE stamp, which the KS4 Autumn schema's
// x-ingress.includable rule writes from P_INCL. The ingress tests pin which codes are includable,
// and the rules-engine integration tests pin each code's decision. The browser checks the journey.
//
// The seeded Kingsmead School pupils on the KS4 Autumn window:
//   * included "Alice Smith" (index 0)       -> not offered, "already included".
//   * non-included "Bob Johnson" (index 201) -> offered, through to the confirmation.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class Ks4AutumnIncludeJourneyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4 Autumn window (DevDataSeeder.KeyStage4AutumnCheckingWindowId).
    private static readonly Guid WindowId = Guid.Parse("7D2E9A14-5C38-4B6F-A0E1-3F8B6C2D4A97");

    private const string NonIncludedPupil = "Bob Johnson";
    private const string IncludedSurname = "Smith";

    [RetryFact]
    [Trait("Category", "Smoke")]
    public async Task NonIncludedPupil_CanBeIncluded_WithoutEvidence_ThroughToTheConfirmation()
    {
        // A leftover request for this pupil would divert the search into the conflict banner.
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await StartIncludeJourneyAsync();
        await SelectPupilAsync(NonIncludedPupil);

        // Evidence is optional for every code: continue with no file and no explanation.
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/page/evidence");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync($"Provide evidence for the inclusion of {NonIncludedPupil}");
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Summary of amendment request");
        await Expect(Page.Locator(".govuk-summary-list")).ToContainTextAsync(NonIncludedPupil);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit request" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/confirmation");
        var panel = await Page.Locator(".govuk-panel").InnerTextAsync();
        Assert.Matches(@"CYPMD_KS4Autumn_[0-9A-F]{7}", panel);
    }

    [RetryFact]
    public async Task IncludedPupil_IsNotOffered_AndTypingTheName_WarnsAlreadyIncluded()
    {
        await StartIncludeJourneyAsync();

        var input = Page.Locator("#pupil-search").First;
        await input.FillAsync(IncludedSurname);
        // Wait for the list to settle, then submit the typed name without picking a suggestion.
        await Page.WaitForTimeoutAsync(500);
        await input.PressAsync("Escape");
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("This pupil is already included");
        Assert.Contains("/already-included", Page.Url);
        await Expect(Page.Locator("table.govuk-table tbody .govuk-table__row").First)
            .ToContainTextAsync("Alice Smith");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task StartIncludeJourneyAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{WindowId}");
        await Page.Locator("input[name='SelectedWhatToChange'][value='Include']").CheckAsync();
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{WindowId}/pupil-search/select-pupil");
    }

    // Types the name, picks the suggestion that carries it, and continues. A suggestion reads
    // "Firstname, Surname, (…)" (PupilSuggestionFormat.Label).
    private async Task SelectPupilAsync(string name)
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
