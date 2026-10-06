using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// Issue 508 — the KS4 June Remove reason "Admitted following permanent exclusion (not registered
// independent schools)" was offered to every school, including independent schools, which cannot
// have been permanently excluded by a school and so cannot action the request. It is now gated on
// SchoolIsNotIndependent, which the journey hides AND the POST endpoint refuses.
//
// The unit tests prove the rule over the real flow JSON and the real condition set. These tests
// prove it end to end, which needs a browser session that IS an independent school: the shared
// fixture impersonates every test as a type "1" school, so this class re-impersonates as type "11"
// on its own Playwright context.
[Collection("E2E")]
public sealed class PermanentExclusionIndependentSchoolTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // Seeded KS4June window whose pupil blob data SeedPupilData uploads (same id as
    // PermanentExclusionCopyTests and Ks4JourneyTests).
    private static readonly Guid SeededWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    // Kingsmead School included pupil — Pincl 401, not an add-back, so the pupil is eligible for
    // the standard removal reasons and the only thing hiding permanent-exclusion is the school type.
    private const string PupilSurname = "Smith";
    private const string PupilFirstName = "Alice";

    private const string PermanentExclusionReason = "permanent-exclusion";

    // The question's own validationFailure — FR-007 requires no new message.
    private const string ReasonValidationMessage =
        "Select a reason for removing Alice Smith from the performance data";

    // Drop the independent-school cookie onto this test's own context. SeedingPageTest has already
    // mirrored the fixture-wide editor cookie in, so this overwrites it by name. Nothing shared is
    // mutated, so the rest of the [Collection("E2E")] run still sees the editor session.
    //
    // Must be `sealed override`, not `new`: xUnit dispatches IAsyncLifetime through the interface
    // map established at SeedingPageTest, so a hidden `new` method here is never called and the
    // independent cookie silently never lands — the tests then run as the fixture editor and report
    // permanent-exclusion as still being offered.
    public sealed override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        var header = await AuthHelpers.ImpersonateAsIndependentAsync(Fixture);
        Assert.NotNull(header);

        var equalsIndex = header.IndexOf('=');
        Assert.True(equalsIndex > 0, $"Malformed impersonation cookie header: '{header}'");

        await Context.AddCookiesAsync([new Cookie
        {
            Name = header[..equalsIndex],
            Value = header[(equalsIndex + 1)..],
            Url = Fixture.BaseUrl
        }]);
    }

    // ── US1: the reason is not offered to an independent school ──────────────

    [RetryFact]
    public async Task IndependentSchool_ReasonPage_OmitsPermanentExclusion()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToReasonPageAsync();

        await Expect(Page.Locator($"input[name='q_reason'][value='{PermanentExclusionReason}']"))
            .ToHaveCountAsync(0);
    }

    [RetryFact]
    public async Task IndependentSchool_ReasonPage_StillOffersNotOnRoll()
    {
        // Proves the independent session really took effect and the page is otherwise intact:
        // "Not on roll" is offered to independent schools (SchoolCanRecordNotOnRoll), so an
        // all-reasons-missing render would fail here.
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToReasonPageAsync();

        await Expect(Page.Locator("input[name='q_reason'][value='not-on-roll']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='q_reason'][value='pupil-died']")).ToHaveCountAsync(1);
    }

    // ── US1: a forged submission is refused, not just hidden (FR-006) ─────────

    [RetryFact]
    public async Task IndependentSchool_CraftedPermanentExclusion_IsRefused()
    {
        await SeedHelpers.CleanupDevRequestsAsync(Fixture.SeedClient);
        await NavigateToReasonPageAsync();

        // The radio is absent from the page, but the POST endpoint still accepts the field name —
        // the bookmark/replay case. Re-inject it into the live form and submit.
        await Page.EvaluateAsync($$"""
            const anchor = document.querySelector("input[name='q_reason']");
            const form = anchor.closest('form');
            const input = document.createElement('input');
            input.type = 'radio';
            input.name = 'q_reason';
            input.value = '{{PermanentExclusionReason}}';
            input.checked = true;
            form.appendChild(input);
            """);

        var injected = Page.Locator($"input[name='q_reason'][value='{PermanentExclusionReason}']");
        await Expect(injected).ToHaveCountAsync(1);
        await injected.CheckAsync(new() { Force = true });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        // Stays on the reason page with the question's own message: the journey did not advance
        // into the permanent-exclusion branch.
        await Expect(Page.Locator(".govuk-error-summary")).ToBeVisibleAsync();
        await Expect(Page.Locator(".govuk-error-summary")).ToContainTextAsync(ReasonValidationMessage);
        Assert.Contains($"/Journey/{SeededWindowId}/page/reason", Page.Url);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // WhatToChange -> Remove -> pupil search -> the reason page (without choosing a reason).
    private async Task NavigateToReasonPageAsync()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{SeededWindowId}");
        await Page.GetByLabel("Remove").First.CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/pupil-search/**");

        var searchInput = Page.Locator("#pupil-search").First;
        await Expect(searchInput).ToBeVisibleAsync();
        await searchInput.FillAsync(PupilSurname);
        var pupil = Page.Locator("li[role='option']").GetByText($"{PupilSurname}, {PupilFirstName}");
        await Expect(pupil).ToBeVisibleAsync();
        await pupil.ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{SeededWindowId}/page/reason");

        // Some reasons are gated on the pupil, so the page only renders options once it is built.
        await Expect(Page.Locator("input[name='q_reason']").First).ToBeVisibleAsync();
    }
}
