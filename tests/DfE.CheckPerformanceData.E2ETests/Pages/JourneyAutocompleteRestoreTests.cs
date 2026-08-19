using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Pages;

// AB#295434: a selected value in the journey's country autocomplete had to be reselected
// after a validation-error reload or back navigation. The fix initialises
// accessible-autocomplete with defaultValue instead of poking the input's DOM value, so
// the component's own re-renders (focus in particular) keep the restored label and the
// suggestion menu stays closed.
//
// Drives the Remove KS4June journey to the EAL details page (country autocomplete + two
// required dates), selects a country, submits with the dates empty to force a validation
// reload, and pins:
//   * the input still shows the selected country;
//   * the suggestion menu is NOT open (it used to pop over the next question);
//   * focusing the input does not wipe the value (the old DOM-poke failure mode —
//     the component re-render reset the input from its empty internal state);
//   * the hidden label field still carries the answer for the next POST.
// Then continues past the page and uses the in-page Back link to pin the same contract
// on a plain GET re-render (bug scenarios 2b/2d share that path).
[Collection("E2E")]
public sealed class JourneyAutocompleteRestoreTests(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    // Seeded KS4June window whose pupil blob data SeedPupilData uploads (same id
    // RequestSubmissionPage uses; DevDataSeeder pins it as a public static).
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // Kingsmead School included pupil — first row in SeedPupilData.
    private const string PupilSurname = "Smith";
    private const string PupilFirstName = "Alice";

    private const string Country = "Trinidad and Tobago";
    private const string CountryInput = "#q_country_originally_from-input";
    private const string CountryLabelField = "#q_country_originally_from-label-value";
    private const string CountryCodeField = "#q_country_originally_from-code-value";
    private const string DetailsPageId = "english-not-first-language-details";

    [RetryFact(3)]
    public async Task CountrySelection_SurvivesValidationErrorReload_AndBackNavigation()
    {
        // No stale DEV-* conflict requests: a leftover conflict for Alice Smith would
        // divert the pupil-search step into the duplicate-attention banner.
        Page.SetDefaultTimeout(60000); // 60 seconds instead of default 30 seconds
        await SeedHelpers.CleanupDevRequestsAsync(_fixture.SeedClient);
        await ImpersonateInBrowserAsync();

        try
        {
            await NavigateToEalDetailsPageAsync();

            // Select the country from the autocomplete.
            var input = Page.Locator(CountryInput);
            await Expect(input).ToBeVisibleAsync();
            await input.FillAsync("Trinidad");
            var suggestion = Page.Locator("li[role='option']").GetByText(Country);
            await Expect(suggestion).ToBeVisibleAsync();
            await suggestion.ClickAsync();
            Assert.Equal(Country, await input.InputValueAsync());

            // Submit with both required dates empty -> validation-error reload (2a).
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
            await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();

            await AssertCountryRestoredAsync("after the validation-error reload");

            // Fill valid dates (cross-field rules: first-English-school <= started-at-school,
            // started-at-school >= 1 Sept four school years back) and continue to evidence.
            await Page.FillAsync("input[name='q_date_pupil_started_day']", "1");
            await Page.FillAsync("input[name='q_date_pupil_started_month']", "9");
            await Page.FillAsync("input[name='q_date_pupil_started_year']", "2023");
            await Page.FillAsync("input[name='q_date_pupil_started_school_in_england_day']", "1");
            await Page.FillAsync("input[name='q_date_pupil_started_school_in_england_month']", "9");
            await Page.FillAsync("input[name='q_date_pupil_started_school_in_england_year']", "2022");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
            await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/evidence");

            // In-page Back link -> plain GET re-render of the details page (2b/2d path).
            await Page.Locator(".govuk-back-link").ClickAsync();
            await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/{DetailsPageId}");

            await AssertCountryRestoredAsync("after the in-page Back link");

            // AB#295434 final review regression check: editing the restored value
            // reopens the menu as normal (the input listener lifts the seeded
            // validChoiceMade), but then leaving WITHOUT picking a suggestion must
            // still close it. The earlier capturing-focus-listener fix left the
            // component's focused state permanently unset, so handleInputBlur became a
            // no-op and the menu stayed open — this is the gap that regressed.
            await input.ClickAsync();
            await input.FillAsync("Trinidad");
            await Expect(Page.Locator("li[role='option']").First).ToBeVisibleAsync();
            await Page.Locator("body").ClickAsync(new() { Force = true });
            Assert.Equal(0, await Page.Locator(".autocomplete__menu--visible").CountAsync());
        }
        catch (Exception ex)
        {
            await Page.ScreenshotAsync(new() { Path = $"{Snapshots.FailuresDir}/AutocompleteRestore_295434.png" });
            throw new Exception($"Autocomplete restore test failed: {ex.Message}", ex);
        }
    }

    private async Task AssertCountryRestoredAsync(string when)
    {
        var input = Page.Locator(CountryInput);

        // Value present without any user interaction.
        Assert.Equal(Country, await input.InputValueAsync());

        // The suggestion menu must not be open over the next question.
        Assert.Equal(0, await Page.Locator(".autocomplete__menu--visible").CountAsync());

        // The decisive regression check: focusing the input triggers an
        // accessible-autocomplete re-render. With the old DOM-poke initialisation the
        // component's internal state was empty and the re-render blanked the field.
        await input.ClickAsync();
        Assert.Equal(Country, await input.InputValueAsync());
        Assert.Equal(0, await Page.Locator(".autocomplete__menu--visible").CountAsync());

        // The hidden label field is what actually posts; it must still carry the answer.
        Assert.Equal(Country, await Page.Locator(CountryLabelField).InputValueAsync());

        // The hidden {field}_code input must round-trip the stored ISO code too — a
        // hard-coded value="" dropped it on every validation-error resubmit (AB#295434).
        Assert.NotEmpty(await Page.Locator(CountryCodeField).InputValueAsync());

        // Release focus so the next steps interact with a settled page. This page has no
        // h1/govuk-heading-l/label--l (the country question is a govuk-label--m sharing the
        // page with two date groups), so click <body> — always present, never the
        // autocomplete input — purely to move focus off the field. Escape first: it now
        // has real, load-bearing meaning inside the app's own JS (AB#295434 final review
        // — it's what the production fix dispatches programmatically on load), so this
        // also exercises the same handleComponentBlur path as a manual sanity check that
        // it doesn't disturb an already-settled, unedited restored field.
        await Page.Keyboard.PressAsync("Escape");
        await Page.Locator("body").ClickAsync(new() { Force = true });
    }

    // Same manual cookie mirroring RequestSubmissionPage uses: PageTest (unlike
    // SeedingPageTest) doesn't copy the impersonation cookie into the browser context.
    private async Task ImpersonateInBrowserAsync()
    {
        var cookie = await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        if (string.IsNullOrEmpty(cookie))
            throw new InvalidOperationException("Impersonation endpoint did not return a cookie");

        var equalsIndex = cookie.IndexOf('=');
        if (equalsIndex <= 0)
            throw new InvalidOperationException($"Invalid cookie format: {cookie}");

        await Context.AddCookiesAsync(new[]
        {
            new Cookie
            {
                Name = cookie[..equalsIndex],
                Value = cookie[(equalsIndex + 1)..],
                Url = _fixture.BaseUrl
            }
        });
    }

    // WhatToChange -> Remove -> pupil search -> reason -> first-language -> EAL details.
    private async Task NavigateToEalDetailsPageAsync()
    {
        await Page.GotoAsync($"{_fixture.BaseUrl}/WhatToChange/{SeededWindowId}");
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

        // Reason: EAL. Select by option value, not label text, so copy edits can't break this.
        await Page.Locator("input[name='q_reason'][value='english-not-first-language']")
            .CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/english-not-first-language");

        // First language: any answer reaches the details page (page-level nextPageId).
        await Page.Locator("input[name='q_first_language'][value='other']")
            .CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/{DetailsPageId}");
    }
}
