using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// AB#027 "Include journey no-results path": when a user on the Include journey's select-pupil
// search types a name, gets no autocomplete hit, and clicks Continue without selecting a pupil,
// the backend looks the typed entry up against BOTH the included and non-included populations and
// branches three ways:
//   * an included match  -> "Already included" warning offering "Abort".
//   * a non-included-only match -> a valid include candidate: the journey proceeds as normal — the
//     search page re-renders with the candidate's suggestions (no warning, no decision page).
//   * no match on either list -> "Pupil not found" page offering "Start adding this pupil" / "Search again".
//   * blank entry        -> existing validation, unchanged (FR-009).
//
// These browser tests drive the seeded Kingsmead School pupil blobs on the KS4June window:
//   * included "Alice Smith" (index 0)       -> the "already included" case (US2).
//   * non-included "Bob Johnson" (index 201) -> the "proceeds as normal" case (US1/FR-004a).
//   * "No Such"                              -> matches nothing on either list -> the "not found" case.
[Collection("E2E")]
public sealed class IncludeNoResultsPathTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded KS4June window (DevDataSeeder.KeyStage4JuneCheckingWindowId), which Kingsmead's
    // pupil blobs are uploaded against.
    private static readonly Guid Ks4JuneWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // Included pupil in the seeded blob (first row of SeedPupilData).
    private const string IncludedSurname = "Smith";

    // A non-included pupil in the seeded blob (index 201) — never on the included list. The
    // Include search matches on a single name token, so tests type the non-included surname.
    private const string NonIncludedSurname = "Johnson";

    // Matches no generated name on either list (Firstnames/Surnames have no "Such" and the
    // deliberate duplicate is "Casey Carter"), so it is the true "neither list" case.
    private const string NoSuchName = "No Such";

    // ── US1: no match on either list -> "Pupil not found" (T011) ─────────────

    [RetryFact(3)]
    public async Task NeitherListMatch_ShowsPupilNotFound_WithBothActions()
    {
        await StartIncludeJourneyAsync();
        await TypePupilWithoutSelectingAsync(NoSuchName);
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
        await TypePupilWithoutSelectingAsync(NoSuchName);
        await ContinueAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Start adding this pupil" }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/learner-details");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue" })).ToBeVisibleAsync();
    }

    [RetryFact(3)]
    public async Task SearchAgain_FromPupilNotFound_ReturnsToIncludeSearch()
    {
        await StartIncludeJourneyAsync();
        await TypePupilWithoutSelectingAsync(NoSuchName);
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

    // ── US1/FR-004a: a non-included-only match proceeds as normal (T015) ─────

    [RetryFact(3)]
    public async Task NonIncludedMatch_ProceedsAsNormal_NoDecisionPage()
    {
await StartIncludeJourneyAsync();
        // Bob Johnson is a non-included pupil — a valid include candidate. The Include search's
        // autocomplete targets NonIncluded pupils, so typing a single surname token ("Johnson" —
        // "Johnson" is a non-included surname bucket and matches no included pupil) and continuing
        // without selecting a suggestion must NOT divert to "Pupil not found" or "Already included":
        // the journey proceeds as normal, re-rendering the search page with the candidate's
        // suggestions visible.
        await TypePupilWithoutSelectingAsync(NonIncludedSurname);
        await ContinueAsync();

        // Not a decision page.
        Assert.DoesNotContain("pupil-not-found", Page.Url);
        Assert.DoesNotContain("already-included", Page.Url);
        // Lands back on the Include search (the ordinary no-selection flow).
        await Expect(Page.Locator("#pupil-search").First).ToBeVisibleAsync();
        Assert.Contains("/pupil-search/select-pupil", Page.Url);
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

        // The matching included pupils are listed so the school can see who was matched. The
        // included "Alice Smith" pupil is always first for a "Smith" search; the exact row count
        // depends on the seeded blob, so assert the known pupil is shown rather than a fragile total.
        var table = Page.Locator("table.govuk-table");
        await Expect(table).ToBeVisibleAsync();
        await Expect(table.Locator("tbody .govuk-table__row").First).ToContainTextAsync("Alice Smith");

        // Both actions are offered: start adding the pupil instead, or abort.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Start adding this pupil" }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Abort" })).ToBeVisibleAsync();
    }

    [RetryFact(3)]
    public async Task StartAddingPupil_FromAlreadyIncluded_LandsOnLearnerDetails()
    {
        await StartIncludeJourneyAsync();
        await TypePupilWithoutSelectingAsync(IncludedSurname);
        await ContinueAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Start adding this pupil" }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/page/learner-details");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Continue" })).ToBeVisibleAsync();
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
    // no-results precondition the decision flow keys on). Escape closes the accessible-autocomplete
    // dropdown without selecting the highlighted option — closing it keeps the Continue button from
    // being obscured by the open suggestion list, which repeatedly re-renders while a query (e.g.
    // "Johnson") keeps populating suggestions.
    private async Task TypePupilWithoutSelectingAsync(string name)
    {
        // accessible-autocomplete keeps the id the config set ("pupil-search") on the text input.
        var input = Page.Locator("#pupil-search").First;
        await Expect(input).ToBeVisibleAsync();
        await input.FillAsync(name);
        // Let the suggestions render, then dismiss the dropdown WITHOUT selecting anything.
        await Page.WaitForTimeoutAsync(250);
        await input.PressAsync("Escape");
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

    }