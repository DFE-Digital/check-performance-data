using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Retrying;

namespace DfE.CheckPerformanceData.E2ETests.Journey;

// The seeded KS2 window (DevDataSeeder.KeyStage2CheckingWindowId) offers one change type.
[Trait("Category", "FullRegression")]
[Collection("E2E")]
public sealed class WhatToChangeSingleOptionTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
{
    private static readonly Guid Ks2WindowId = Guid.Parse("B4F61C83-2D7A-4E95-8C30-6A1E9F5B2D48");

    [RetryFact]
    public async Task Ks2Window_SkipsTheChangeTypePage_AndStartsTheJourney()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Ks2WindowId}");

        await Page.WaitForURLAsync($"**/Journey/{Ks2WindowId}/**");
        Assert.Equal(0, await Page.Locator("input[name='SelectedWhatToChange']").CountAsync());
    }

    [RetryFact]
    public async Task Ks2Window_FirstPageBackLink_GoesToCheckYourPupilData()
    {
        await Page.GotoAsync($"{Fixture.BaseUrl}/WhatToChange/{Ks2WindowId}");
        await Page.WaitForURLAsync($"**/Journey/{Ks2WindowId}/**");

        await Page.Locator("a.govuk-back-link").ClickAsync();

        await Page.WaitForURLAsync($"**/CheckYourPupilData/{Ks2WindowId}");
    }
}
