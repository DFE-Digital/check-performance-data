using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// KS4 "Remove" journeys — AB#295246 (removal date validation). The single removal/exclusion
// date must be a real calendar date no later than today:
//   * a future date is blocked with the journey-specific "Date … must be in the past" message
//     (RemovalJourneyDateRules, run by JourneyValidationService.ValidatePageDates);
//   * an invalid date (blank/part-filled, non-4-digit year, impossible calendar date) is blocked
//     with the question's "Enter the date …" message (the scoped ValidateAnswer fallback);
//   * today and any past date are accepted and the journey advances.
//
// The date-validation behaviour is what these tests pin; each scenario drives a removal journey
// to its date page, submits, and asserts either the error summary or advancement.
[Collection("E2E")]
public sealed class Ks4JourneyTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Seeded KS4June window whose pupil blob data SeedPupilData uploads (same id
    // RequestSubmissionPage and JourneyAutocompleteRestoreTests use).
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // Kingsmead School included pupil — first row in SeedPupilData.
    private const string PupilSurname = "Smith";
    private const string PupilFirstName = "Alice";

    // Far enough in the future that it is later than the app's "today" no matter when the test
    // runs, while still being a well-formed calendar date (so only the future-date rule fires).
    private const int FutureYear = 2099;

    // The removal journeys whose date page is a single Date question and whose reason option is
    // always visible (not gated by PupilIsNotAddBack): pupil-died and elective-home-education.
    // The other removal pages (permanent-exclusion, child-missing-education, permanently-excluded,
    // permanently-left-england-questions) precede the date with required non-date questions, so
    // they are covered at the unit level (RemovalJourneyDateRulesTests / dispatch theory) instead.
    public static IEnumerable<object[]> RemovalJourneyCases()
    {
        // Each journey lands on a different page after the date: pupil-died goes straight to the
        // summary, elective-home-education to the evidence page.
        yield return new object[] { new JourneyCase("pupil-died", "pupil-died", "Summary of amendment request") };
        yield return new object[] { new JourneyCase("elective-home-education", "elective-home-education", "Provide evidence for the removal of Alice Smith") };
    }

    // ── US1: a future removal date is blocked with the journey's exact message ──

    [RetryTheory(3)]
    [MemberData(nameof(RemovalJourneyCases))]
    public async Task RemoveFlow_FutureDate_ShowsMustBeInThePast(JourneyCase journey)
    {
        // No stale DEV-* conflict requests: a leftover conflict for Alice Smith would divert the
        // pupil-search step into the duplicate-attention banner.
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToRemovalDatePageAsync(journey);
        await FillDateAsync("date-removed-from-roll", day: 15, month: 6, year: FutureYear);
        await ContinueAsync();

        await AssertDateBlockedAsync(
            "Date Alice Smith was removed from your school roll must be in the past");
    }

    // ── US2: an invalid calendar date is blocked with the journey's own wording ──

    [RetryTheory(3)]
    [MemberData(nameof(RemovalJourneyCases))]
    public async Task RemoveFlow_InvalidDate_ShowsEnterTheDateMessage(JourneyCase journey)
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        await NavigateToRemovalDatePageAsync(journey);
        await FillDateAsync("date-removed-from-roll", day: 31, month: 2, year: 2026);
        await ContinueAsync();

        await AssertDateBlockedAsync(
            "Enter the date Alice Smith was removed from your school roll");
    }

    // ── US3: today and any past date are accepted and the journey advances ──

    [RetryTheory(3)]
    [MemberData(nameof(RemovalJourneyCases))]
    public async Task RemoveFlow_TodaysDate_AdvancesToSummary(JourneyCase journey)
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        var today = UkToday();
        await NavigateToRemovalDatePageAsync(journey);
        await FillDateAsync("date-removed-from-roll", today.Day, today.Month, today.Year);
        await ContinueAsync();

        // "Later than today", not "before today" — a pupil removed from the roll today is valid.
        await AssertAdvancedPastDatePageAsync(journey);
    }

    [RetryTheory(3)]
    [MemberData(nameof(RemovalJourneyCases))]
    public async Task RemoveFlow_PastDate_AdvancesToSummary(JourneyCase journey)
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);

        // No lower limit — a removal from years ago is accepted.
        await NavigateToRemovalDatePageAsync(journey);
        await FillDateAsync("date-removed-from-roll", day: 1, month: 9, year: 2010);
        await ContinueAsync();

        await AssertAdvancedPastDatePageAsync(journey);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    public sealed record JourneyCase(string ReasonValue, string PageId, string AdvancedHeading);

    // WhatToChange -> Remove -> pupil search -> reason -> the removal date page. The reason
    // radio's value doubles as the next page id for these journeys (PageId = ReasonValue).
    private async Task NavigateToRemovalDatePageAsync(JourneyCase journey)
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{SeededWindowId}");
        await Page.GetByLabel("Remove").First.CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/pupil-search/**");

        // Pick the seeded pupil through the pupil autocomplete.
        var searchInput = Page.Locator("#pupil-search").First;
        await Expect(searchInput).ToBeVisibleAsync();
        await searchInput.FillAsync(PupilSurname);
        var pupil = Page.Locator("li[role='option']").GetByText($"{PupilSurname}, {PupilFirstName}");
        await Expect(pupil).ToBeVisibleAsync();
        await pupil.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/reason");

        // Select by option value, not label text, so copy edits can't break this.
        await Page.Locator($"input[name='q_reason'][value='{journey.ReasonValue}']")
            .CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/{journey.PageId}");
    }

    // QuestionPartialModel renders date inputs as q_<id>_day/_month/_year, where the question
    // id's dashes become underscores.
    private async Task FillDateAsync(string fieldName, int day, int month, int year)
    {
        var baseId = $"q_{fieldName.Replace("-", "_")}";
        await Page.Locator($"#{baseId}_day").FillAsync(day.ToString());
        await Page.Locator($"#{baseId}_month").FillAsync(month.ToString());
        await Page.Locator($"#{baseId}_year").FillAsync(year.ToString());
    }

    private async Task ContinueAsync() =>
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

    // Mirrors JourneyValidationService.UkToday: the app compares against the UK calendar date,
    // not UTC, so the test must compute "today" the same way (notably just after midnight BST).
    private static DateOnly UkToday() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/London")));

    private async Task AssertAdvancedPastDatePageAsync(JourneyCase journey)
    {
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync(journey.AdvancedHeading);
    }

    // The date-rule violation renders as a GOV.UK error summary (the question's date_consistent
    // ModelState error) and the URL stays on the date page.
    private async Task AssertDateBlockedAsync(string expectedMessage)
    {
        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        var summaryText = await Page.Locator(".govuk-error-summary").InnerTextAsync();
        Assert.Contains(expectedMessage, summaryText);

        Assert.Contains("/page/", Page.Url);
        Assert.DoesNotContain("/page/evidence", Page.Url);
    }
}
