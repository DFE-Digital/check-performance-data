using System.Net;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// #475: the content editor's Save button keeps the page's search body. Driven over HTTP, the way
// the editor's forms post: make a page, give a heading a word that is nowhere else, press Save,
// publish, and search the site for the word. Before the fix the Save wrote an empty body, and
// the search found nothing.
[Trait("Category", "FullRegression")]
[Collection("Http")]
public sealed class ContentEditorSaveButtonTests(PlaywrightFixture fixture)
{
    private readonly PlaywrightFixture _fixture = fixture;

    [Fact]
    public async Task APageSavedWithTheSaveButtonAndThenPublished_IsFoundByAWordInItsBody()
    {
        // Lowercase hex: one search token, and no stop word.
        var word = "cypdsave" + Guid.NewGuid().ToString("N")[..12].ToLowerInvariant();
        var segment = $"e2e-save-button-{Guid.NewGuid():N}";
        Guid? pageId = null;
        try
        {
            // The title does not hold the word, so a hit can only come from the body.
            pageId = await CmsSeedHelpers.CreatePageNodeAsync(
                _fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E save button page");
            await CmsSeedHelpers.AddWidgetAsync(_fixture.SeedClient, pageId.Value, "0.0", "heading");
            await CmsSeedHelpers.UpdateWidgetAsync(_fixture.SeedClient, pageId.Value, "0.0", "heading",
                new Dictionary<string, string> { ["level"] = "2", ["text"] = $"Return the {word} census" });

            await CmsSeedHelpers.SaveDraftAsync(_fixture.SeedClient, pageId.Value);
            await CmsSeedHelpers.PublishDraftAsync(_fixture.SeedClient, pageId.Value);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}/search?q={word}");
            var response = await _fixture.SeedClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var results = await response.Content.ReadAsStringAsync();

            Assert.Contains($"href=\"{FixtureContent.RootPath}/{segment}\"", results);
        }
        finally
        {
            if (pageId is { } created)
            {
                await CmsSeedHelpers.TryDeletePageAsync(_fixture.SeedClient, created);
            }
        }
    }
}
