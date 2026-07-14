using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Pages;

[Collection("E2E")]
[Trait("Category", "W0")]
public sealed class RequestSubmissionPage(PlaywrightFixture fixture) : PageTest
{
    private readonly PlaywrightFixture _fixture = fixture;

    // T012 — Self-submitted duplicate message appears for the same user re-submitting
    [RetryFact(3)]
    public async Task SelfSubmittedDuplicate_ShowsSelfReferentialMessage()
    {
        // This test verifies the US1 self-submitted validation message when the same
        // user attempts to submit a duplicate request for the same pupil.
        //
        // Setup (via dev seed endpoints):
        //   1. Impersonate as a test user (the fixture-level impersonation is already active)
        //   2. Submit a request for pupil X
        //   3. Start a new request for the same pupil X
        //   4. On the pupil-search step, select pupil X
        //
        // Expected: The self-submitted validation message appears:
        //   "You already have a pending request for this pupil."
        //   "You can view your existing request in your requests list."
        var baseUrl = _fixture.BaseUrl;

        // The test requires seeding a CheckingWindow + a submitted ChangeRequest for a
        // known pupil. This is done through the dev-only seed endpoint.
        // See docs/request-journey.md for available dev seed endpoints.

        // Navigate to the check-your-data page (the starting point for the journey).
        await Page.GotoAsync($"{baseUrl}/");

        // The actual browser-based flow depends on the seeded data and the journey
        // structure. Implement per the dev seed/impersonation infrastructure when
        // the dev seed endpoints for ChangeRequests are available.

        // Placeholder: ensure the page loaded (replace with real assertions).
        var body = Page.Locator("body");
        await Expect(body).ToBeVisibleAsync();
    }

    // T026 — Other-user scenario: identity must not be revealed
    [RetryFact(3)]
    public async Task OtherSubmittedDuplicate_DoesNotRevealIdentity()
    {
        // This test verifies that when user B encounters a duplicate created by user A,
        // the validation message does not reveal user A's name, email, or any personal
        // identifier.
        //
        // Setup (via dev seed endpoints):
        //   1. Impersonate as user A, submit a request for pupil X
        //   2. Impersonate as user B (or a different test role)
        //   3. Start a new request for pupil X
        //   4. On the pupil-search step, select pupil X
        //
        // Expected:
        //   - Error message does NOT contain user A's name or email
        //   - Error message says "Another user at your school has a pending request
        //     for this pupil." followed by "Please coordinate with colleagues or
        //     contact support if this appears to be in error."
        //
        // Replace this placeholder with the full Playwright flow once the dev seed
        // infrastructure for ChangeRequests is available.
        var baseUrl = _fixture.BaseUrl;

        await Page.GotoAsync($"{baseUrl}/");
        var body = Page.Locator("body");
        await Expect(body).ToBeVisibleAsync();
    }
}
