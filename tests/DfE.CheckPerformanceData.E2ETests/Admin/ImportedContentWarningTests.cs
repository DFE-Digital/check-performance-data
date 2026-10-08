using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// Pages listed in Data/Import/manifest.json are imported at start-up and can be overwritten by
// the next release. The editor says so at the top of such a page, and only such a page: a
// warning on every page would be ignored, and none on these would cost somebody their work.
//
// The guide "How to use the CMS" is imported into every environment, so its home page is a page
// the warning must appear on wherever this suite runs. /help/not-found is created by the service
// but belongs to the editors, so it is one the warning must not appear on.
[Collection("E2E")]
public sealed class ImportedContentWarningTests(PlaywrightFixture fixture)
{
    private const string GuideHomeId = "a969611f-33ad-518d-9ce5-dcd82a9b2656";
    private const string HelpNotFoundId = "00000000-cd94-4a01-8f01-00000000000f";
    private const string Warning = "Changes you make here will be overwritten when the service is updated.";

    private readonly PlaywrightFixture _fixture = fixture;

    private async Task<string> EditorAsync(string pageId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{_fixture.BaseUrl}/admin/pages/{pageId}/edit");

        var response = await TestHttpClients.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Editor_WarnsThatAnImportedPageWillBeOverwritten()
    {
        var body = await EditorAsync(GuideHomeId);

        Assert.Contains("This page is supplied with the service.", body);
        Assert.Contains(Warning, body);
        Assert.Contains("cms-guide.json", body);
    }

    [Fact]
    public async Task Editor_DoesNotWarn_OnAPageThatIsNotImported()
    {
        var body = await EditorAsync(HelpNotFoundId);

        Assert.DoesNotContain("supplied with the service", body);
    }
}
