using DfE.CheckPerformanceData.E2ETests.Fixtures;
using Microsoft.Playwright;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// What to change on a 16-19 (Post16) window offers exactly the changes with a flow file: Include
// (#711), Merge and Remove. Add has no 16-19 flow, so it is not offered and a crafted post of it is
// refused server-side (docs/ks2-journeys.md, decision 1). A KS4 June window keeps all four.
//
// The seeded "16 to 19 Oct" window (DevDataSeeder.Post16OctoberCheckingWindowId) and the seeded
// KS4June window (DevDataSeeder.KeyStage4JuneCheckingWindowId) — the same ids AddPupilJourneyTests uses.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class WhatToChangePost16OptionsTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private static readonly Guid Post16WindowId = Guid.Parse("EC6493B8-9B66-4090-8BE6-9DFC3805751F");
    private static readonly Guid Ks4JuneWindowId = Guid.Parse("F34D285B-8660-4D12-9C30-787328DEAA0A");

    [RetryFact]
    [Trait("Category", "Smoke")]
    public async Task Post16Window_OffersIncludeMergeAndRemove_AndNoAdd()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Post16WindowId}");

        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Include']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Merge']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Remove']")).ToHaveCountAsync(1);
        Assert.Equal(0, await Page.Locator("input[name='SelectedWhatToChange'][value='Add']").CountAsync());

        // The 16-19 noun.
        await Expect(Page.GetByText("Include a student in data")).ToBeVisibleAsync();
    }

    [RetryFact]
    public async Task Post16Window_SelectingInclude_OpensTheIncludeJourney()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Post16WindowId}");

        await Page.Locator("input[name='SelectedWhatToChange'][value='Include']").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

        await Page.WaitForURLAsync($"**/Journey/{Post16WindowId}/pupil-search/select-pupil");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 }))
            .ToContainTextAsync("What is the name of the student to be included?");
        await Expect(Page.Locator("#pupil-search").First).ToBeVisibleAsync();
    }

    [RetryFact]
    public async Task Ks4JuneWindow_StillOffersInclude()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Ks4JuneWindowId}");

        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Include']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Merge']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Remove']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("input[name='SelectedWhatToChange'][value='Add']")).ToHaveCountAsync(1);

        await Page.Locator("input[name='SelectedWhatToChange'][value='Include']").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync($"**/Journey/{Ks4JuneWindowId}/pupil-search/select-pupil");
        await Expect(Page.Locator("#pupil-search").First).ToBeVisibleAsync();
    }

    [RetryFact]
    public async Task Post16Window_CraftedAddSubmission_IsRefused()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Post16WindowId}");

        // The radio is not on the page, but the POST endpoint still exists. Re-inject an Add radio
        // into the live form — the bookmark/replay case — select it, and submit.
        await Page.EvaluateAsync("""
            const input = document.createElement('input');
            input.type = 'radio';
            input.name = 'SelectedWhatToChange';
            input.value = 'Add';
            document.querySelector('input[name="SelectedWhatToChange"][value="Merge"]')
                .closest('form').appendChild(input);
            input.checked = true;
            """);

        var injected = Page.Locator("input[name='SelectedWhatToChange'][value='Add']");
        await Expect(injected).ToHaveCountAsync(1);
        await injected.CheckAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();

        // No journey starts; the user is returned to the student data page.
        await Page.WaitForURLAsync($"**/CheckYourPupilData/{Post16WindowId}");
        Assert.False(Page.Url.Contains("/Journey/", StringComparison.OrdinalIgnoreCase));
    }
}
