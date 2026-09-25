using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using xRetry;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// #439: the Include amendment option is not offered on a 16-19 (Post16) window and a crafted
// submission of it is refused server-side, while a KS4 window keeps the option exactly as before.
//
// The seeded "16 to 19 Oct" window (DevDataSeeder.Post16OctoberCheckingWindowId) and the seeded KS4June window
// (DevDataSeeder.KeyStage4JuneCheckingWindowId) — the same ids AddPupilJourneyTests uses.
[Collection("E2E")]
public sealed class WhatToChangePost16OptionsTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    // The seeded "16 to 19 Oct" window — Include must not be offered (US1) and a crafted Include post
    // must be refused (US3).
    private static readonly Guid Post16WindowId = Guid.Parse("EC6493B8-9B66-4090-8BE6-9DFC3805751F");

    // The seeded KS4June window — Include must still be offered (US2).
    private static readonly Guid Ks4JuneWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    [RetryFact(3)]
    public async Task Post16Window_ShowsOnlyMergeAndRemove()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Post16WindowId}");

        // Exactly two radios: Merge and Remove.
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Merge']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Remove']")).ToHaveCountAsync(1);

        // No Include (this feature) and no Add (pre-existing AB#297310 gate) anywhere.
        Assert.Equal(0, await Page.Locator("input[name='SelectedWhatToChange'][value='Include']").CountAsync());
        Assert.Equal(0, await Page.Locator("input[name='SelectedWhatToChange'][value='Add']").CountAsync());
    }

    [RetryFact(3)]
    public async Task Ks4JuneWindow_StillOffersInclude()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Ks4JuneWindowId}");

        // The option must be present and selectable next to Merge/Remove/Add exactly as before.
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Include']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Merge']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Remove']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Add']")).ToHaveCountAsync(1);

        // Selecting Include and continuing still opens the Include journey.
        await Page.Locator("input[name='SelectedWhatToChange'][value='Include']").CheckAsync(new() { Force = true });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/pupil-search/select-pupil");
        await Expect(Page.Locator("#pupil-search").First).ToBeVisibleAsync();
    }

    [RetryFact(3)]
    public async Task Post16Window_CraftedIncludeSubmission_IsRefused()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Post16WindowId}");

        // The radio is gone from the page, but the POST endpoint still exists. Re-inject the removed
        // Include radio into the live form — the bookmark/replay case — select it, and submit.
        await Page.EvaluateAsync("""
            const input = document.createElement('input');
            input.type = 'radio';
            input.name = 'SelectedWhatToChange';
            input.value = 'Include';
            document.querySelector('input[name="SelectedWhatToChange"][value="Merge"]')
                .closest('form').appendChild(input);
            input.checked = true;
            """);

        // Check it is selected with the form prepared.
        var injected = Page.Locator("input[name='SelectedWhatToChange'][value='Include']");
        await Expect(injected).ToHaveCountAsync(1);
        await injected.CheckAsync(new() { Force = true });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

        // No journey starts; the user is returned to the student data page.
        await Page.WaitForURLAsync($"**/CheckYourPupilData/{Post16WindowId}");
        Assert.False(Page.Url.Contains("/Journey/", StringComparison.OrdinalIgnoreCase));
    }
}