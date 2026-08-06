using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Pages;

[Collection("E2E")]
public sealed class RequestSubmissionPage(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    // The synthetic impersonation user's NameIdentifier (DevImpersonationTicketBuilder).
    private static readonly Guid ImpersonatedUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // The dev pipeline runner hardcodes SubmittedByName to "Dev Harness".
    private const string DevHarnessName = "Dev Harness";

    // Kingsmead School (impersonated user's org) included pupil – first row in SeedPupilData.
    private const string PupilFirstName = "Alice";
    private const string PupilSurname = "Smith";
    private const string PupilUpn = "A86040700001B";

    // Use the seeded KeyStage4June checking window where pupil data is uploaded to blob
    // storage by SeedPupilData. The DevWindowId (dddddddd-...) has no pupil blob data,
    // so the autocomplete returns nothing.
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    [RetryFact(3)]
    public async Task SelfSubmittedDuplicate_ShowsYouHaveAlreadySubmittedMessage()
    {
        // Arrange – seed a conflicting ChangeRequest submitted BY the impersonated user.
        await SeedConflictForUserAsync(ImpersonatedUserId);

        // Authenticate in the Playwright browser (PageTest doesn't mirror cookies automatically).
        await ImpersonateInBrowserAsync();

        // Act – navigate the journey to the pupil selection and pick the conflicted pupil.
        await NavigateToPupilSearchAsync();
        await SearchAndSelectPupilAsync(PupilFirstName, PupilSurname);

        // Assert – the attention banner should show the self-submitted message.
        // The PupilSearch view uses moj-alert--warning, NOT govuk-notification-banner.
        try
        {
            var banner = Page.Locator(".moj-alert--warning").First;
            await Expect(banner).ToBeVisibleAsync();

            var bannerText = await banner.InnerTextAsync();
            // Self-submitted: "You have already submitted a …"
            Assert.Contains("You have already submitted", bannerText);
            // Self-submitted does NOT reveal any colleague's name.
            Assert.DoesNotContain(DevHarnessName, bannerText);
        }
        catch (Exception ex)
        {
            await Page.ScreenshotAsync(new() { Path = $"{Snapshots.FailuresDir}/SelfSubmitted_ShowsYouHaveAlreadySubmitted.png" });
            throw new Exception($"SelfSubmittedDuplicate test failed: {ex.Message}", ex);
        }
    }

    [RetryFact(3)]
    public async Task OtherSubmittedDuplicate_ShowsColleagueName()
    {
        // Arrange – seed a conflicting ChangeRequest submitted by a DIFFERENT user.
        var otherUserId = Guid.NewGuid();
        await SeedConflictForUserAsync(otherUserId);

        // Authenticate in the Playwright browser (PageTest doesn't mirror cookies automatically).
        await ImpersonateInBrowserAsync();

        // Act – navigate the journey to the pupil selection and pick the conflicted pupil.
        await NavigateToPupilSearchAsync();
        await SearchAndSelectPupilAsync(PupilFirstName, PupilSurname);

        // Assert – the attention banner should show the other-submitted message with the colleague's name.
        try
        {
            var banner = Page.Locator(".moj-alert--warning").First;
            await Expect(banner).ToBeVisibleAsync();

            var bannerText = await banner.InnerTextAsync();
            // Other-submitted: "Your colleague Dev Harness has already submitted …"
            Assert.Contains("Your colleague", bannerText);
            Assert.Contains(DevHarnessName, bannerText);
        }
        catch (Exception ex)
        {
            await Page.ScreenshotAsync(new() { Path = $"{Snapshots.FailuresDir}/OtherSubmitted_ShowsColleagueName.png" });
            throw new Exception($"OtherSubmittedDuplicate test failed: {ex.Message}", ex);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // Hit the dev impersonation endpoint, then inject the cookie into the Playwright
    // browser context so Page.GotoAsync requests authenticate as editor. (SeedingPageTest
    // does this automatically; PageTest does not, so we must do it manually here.)
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

    private async Task SeedConflictForUserAsync(Guid userId)
    {
        // Delete any DEV-* ChangeRequests left by previous tests to prevent stale
        // conflicts from poisoning this test (e.g. SelfSubmittedDuplicate seeds a
        // request for ImpersonatedUserId that OtherSubmittedDuplicate would find first).
        await SeedHelpers.CleanupDevRequestsAsync(_fixture.SeedClient);

        await SeedHelpers.SeedConflictRequestAsync(
            _fixture.SeedClient,
            userId,
            PupilUpn,
            DevHarnessName,
            SeededWindowId);
    }

    // Navigate the full journey: WhatToChange -> pupil search.
    // Skips the landing page because dev-impersonated users are challenged by LandingPage
    // (no org claim), so we go directly to WhatToChange with the hardcoded dev window id.
    private async Task NavigateToPupilSearchAsync()
    {
        // Navigate to WhatToChange/{windowId} and select Remove.
        await Page.GotoAsync($"{_fixture.BaseUrl}/WhatToChange/{SeededWindowId}");
        await Expect(Page.Locator("body")).ToBeVisibleAsync();

        // Select "Remove" and submit the WhatToChange form.
        var removeRadio = Page.GetByLabel("Remove").First;
        await removeRadio.CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        // Wait for navigation to the pupil search page (/Journey/{windowId}/pupil-search/...).
        var escapedBase = System.Text.RegularExpressions.Regex.Escape(_fixture.BaseUrl);
        var escapedWindowId = System.Text.RegularExpressions.Regex.Escape(SeededWindowId.ToString());
        await Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex($"^{escapedBase}/Journey/{escapedWindowId}/pupil-search/"));
    }

    // Type the pupil name into the accessible autocomplete and click Continue.
    // The PupilSearch view uses accessibleAutocomplete which creates an input with
    // id="pupil-search" inside #pupil-search-container. Selecting a suggestion via
    // the autocomplete populates the hidden selectedPupilId field.
    private async Task SearchAndSelectPupilAsync(string firstName, string surname)
    {
        // The autocomplete input has id="pupil-search" created by accessibleAutocomplete.
        var searchInput = Page.Locator("#pupil-search").First;
        await Expect(searchInput).ToBeVisibleAsync();
        await searchInput.FillAsync($"{surname}");
        await Page.WaitForTimeoutAsync(1000); // Wait for the autocomplete to fetch suggestions.
        // Wait for the autocomplete suggestion list to appear, then select the pupil.
        // accessibleAutocomplete renders suggestions as <li> elements in an <ul> menu.
        var suggestion = Page.Locator("li[role='option']").GetByText($"{surname}, {firstName}");
        await Expect(suggestion).ToBeVisibleAsync();
        await suggestion.ClickAsync();

        // Now submit the form by clicking Continue.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        // The page will re-render with either the next journey step or the duplicate attention banner.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}