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

    [RetryFact(3)]
    public async Task SelfSubmittedDuplicate_ShowsSelfReferentialMessage()
    {
        await _fixture.SeedUser("testuser1", "Test", "User1", "school1");
        await _fixture.SeedUser("userB", "Other", "User", "school1");
        await _fixture.SeedChangeRequest("testuser1", "userB", "pupil1", "submitteduncommitted");

        await Page.GotoAsync($"{_fixture.BaseUrl}/");
        
        await Expect(Page.Locator("body")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Colleague name")).ToBeVisibleAsync();
        await Expect(Page.GetByText("userB")).ToBeVisibleAsync();
    }

    [RetryFact(3)]
    public async Task OtherSubmittedDuplicate_DoesNotRevealIdentity()
    {
        await _fixture.SeedUser("testuser1", "Test", "User1", "school1");
        await _fixture.SeedUser("userB", "Other", "User", "school1");
        await _fixture.SeedChangeRequest("userB", "testuser1", "pupil1", "submitteduncommitted");

        await Page.GotoAsync($"{_fixture.BaseUrl}/");
        
        await Expect(Page.Locator("body")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Attention banner")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Test User")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByText("userB")).Not.ToBeVisibleAsync();
    }
}
