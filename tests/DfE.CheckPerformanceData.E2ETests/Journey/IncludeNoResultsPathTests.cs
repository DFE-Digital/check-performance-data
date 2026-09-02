using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#027 "Include journey no-results path": when a user on the Include journey's select-pupil
// search types a name, gets no autocomplete hit, and clicks Continue without selecting a pupil,
// the backend looks the typed entry up against the included population instead of showing the
// bare "Enter the name of the pupil to be included." validation.
//   * no included match  -> "Pupil not found" page offering "Start adding this pupil" / "Search again".
//   * an included match  -> "Already included" warning offering "Abort" (the autocomplete, which
//     targets NonIncluded pupils, never surfaces it — this is exactly the scenario US2 targets).
//   * blank entry        -> existing validation, unchanged (FR-009).
//
// These browser tests drive the seeded Kingsmead School pupil blobs on the KS4June window:
//   * included "Alice Smith" (index 0)   -> the "already included" case.
//   * non-included "Bob Johnson" (index 201) -> not on the included list -> the "not found" case.
[Collection("E2E")]
public sealed class IncludeNoResultsPathTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4June window (DevDataSeeder.KeyStage4JuneCheckingWindowId), which Kingsmead's
    // pupil blobs are uploaded against.
    private static readonly Guid Ks4JuneWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // Included pupil in the seeded blob (first row of SeedPupilData).
    private const string IncludedSurname = "Smith";

    // A non-included pupil in the seeded blob (index 201) — never on the included list.
    private const string NonIncludedSurname = "Johnson";
    private const string NonIncludedFirstName = "Bob";

    // ── US1: no included match -> "Pupil not found" (T012) ──────────────────

    [RetryFact(3)]
    public async Task NoIncludedMatch_ShowsPupilNotFound_WithBothActions()
    {
        await StartIncludeJourneyAsync();
        await TypePupilWithoutSelectingAsync($"{NonIncludedFirstName} {NonIncludedSurname}");
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Pupil not found");
        Assert.Contains("/pupil-not-found", Page.Url);

        // Both actions are offered.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Start adding this pupil" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Search again" }))
            .ToBeVisibleAsync();
    }

    [RetryFact(3)]
    public async Task StartAddingPupil_FromPupilNotFound_LandsOnLearnerDetails()
    {
        await StartIncludeJourneyAsync();
        await TypePupilWithoutSelectingAsync($"{NonIncludedFirstName} {NonIncludedSurname}");
        await ContinueAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Start adding this pupil" }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/learner-details");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue" })).ToBeVisibleAsync();
    }

    [RetryFact(3)]
    public async Task SearchAgain_FromPupilNotFound_ReturnsToIncludeSearch()
    {
        await StartIncludeJourneyAsync();
        await TypePupilWithoutSelectingAsync($"{NonIncludedFirstName} {NonIncludedSurname}");
        await ContinueAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Search again" }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/pupil-search/select-pupil");
        // The pupil-search autocomplete input (accessible-autocomplete keeps the configured id).
        await Expect(Page.Locator("#pupil-search").First).ToBeVisibleAsync();
    }

    [RetryFact(3)]
    public async Task BlankEntry_KeepsExistingValidationMessage()
    {
        await StartIncludeJourneyAsync();
        await ContinueAsync();

        var summary = Page.Locator(".govuk-error-summary");
        await Expect(summary).ToBeVisibleAsync();
        var text = await summary.InnerTextAsync();
        Assert.Contains("Enter the name of the pupil to be included.", text);
        // No redirect to a decision page.
        Assert.DoesNotContain("pupil-not-found", Page.Url);
        Assert.DoesNotContain("already-included", Page.Url);
    }

    // ── US2: an included match -> "Already included" warning (T020/T021) ─────

    [RetryFact(3)]
    public async Task IncludedMatch_ShowsAlreadyIncluded_WithAbortOption()
    {
        await StartIncludeJourneyAsync();
        // Alice Smith is included. The Include search autocomplete targets NonIncluded pupils and
        // the feature's included-list lookup reuses the single-field name matcher, so we type a
        // partial surname — "the user typed a partial name" (US2) — that resolves to her, and
        // submit WITHOUT selecting a suggestion.
        await TypePupilWithoutSelectingAsync(IncludedSurname);
        await ContinueAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("This pupil is already included");
        Assert.Contains("/already-included", Page.Url);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Abort" })).ToBeVisibleAsync();
    }

    [RetryFact(3)]
    public async Task Abort_FromAlreadyIncluded_ReturnsToCheckYourPupilData()
    {
        await StartIncludeJourneyAsync();
        await TypePupilWithoutSelectingAsync(IncludedSurname);
        await ContinueAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Abort" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("Check your pupil data");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task StartIncludeJourneyAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Ks4JuneWindowId}");
        await Page.Locator("input[name='SelectedWhatToChange'][value='Include']").CheckAsync(new() { Force = true });
        await ContinueAsync();
        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/pupil-search/select-pupil");
    }

    // Types into the pupil-search autocomplete but deliberately does NOT pick a suggestion, so
    // selectedPupilId stays empty while selectedPupilLabel carries the typed name (the exact
    // no-results precondition the decision flow keys on).
    private async Task TypePupilWithoutSelectingAsync(string name)
    {
        // accessible-autocomplete keeps the id the config set ("pupil-search") on the text input.
        var input = Page.Locator("#pupil-search").First;
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync(name);
        // A tiny settle ensures the suggestion list has rendered; we leave it unopened by just
        // continuing from the field so selectedPupilId stays empty.
        await Page.WaitForTimeoutAsync(250);
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

    }