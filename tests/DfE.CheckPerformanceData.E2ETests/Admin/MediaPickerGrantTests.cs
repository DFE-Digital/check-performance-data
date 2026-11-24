using System.Net;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Admin;

// The media picker needs the media section. The test takes Media away from the editor role in
// Role settings, then checks as the editor that:
// - the picker endpoints give 404;
// - the page editor renders no picker dialog and no link to the library, but keeps the Image
//   widget's image id field;
// - the Image widget still saves with a typed image id.
// It gives the grant back in a finally block. The change is stack-wide, so the test runs in the
// isolated collection, never beside other tests.
[Trait("Category", "FullRegression")]
[Collection("Isolated")]
public sealed partial class MediaPickerGrantTests(PlaywrightFixture fixture)
{
    private const string EditorRole = "cypmd_content_access_user";
    private const string RolesPath = "/admin/system/roles";

    private readonly PlaywrightFixture _fixture = fixture;

    [GeneratedRegex(@"name=""Grants\[" + EditorRole + @"\]""\s+value=""([^""]+)""\s+checked")]
    private static partial Regex EditorGrant();

    private Task<HttpResponseMessage> GetAsync(string path) =>
        _fixture.SeedClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"{_fixture.BaseUrl}{path}"));

    private async Task<List<string>> EditorGrantsAsync()
    {
        using var response = await GetAsync(RolesPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        return EditorGrant().Matches(html).Select(m => m.Groups[1].Value).ToList();
    }

    private async Task SaveEditorGrantsAsync(IEnumerable<string> sections)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(_fixture.SeedClient, RolesPath);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("Roles", EditorRole),
        };
        fields.AddRange(sections.Select(s => new KeyValuePair<string, string>($"Grants[{EditorRole}]", s)));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_fixture.BaseUrl}{RolesPath}")
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Add("Cookie", cookie);
        using var response = await _fixture.SeedClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task AnEditorWithoutTheMediaSection_GetsNoPicker_AndCanStillTypeAnImageId()
    {
        await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        var sample = await MediaSeedHelpers.SeedSampleAsync(_fixture.SeedClient, "png");
        var segment = $"e2e-picker-grant-{Guid.NewGuid():N}";
        var pageId = await CmsSeedHelpers.CreatePageNodeAsync(_fixture.SeedClient, FixtureContent.RootId, "content", segment, "E2E picker grant");
        await CmsSeedHelpers.AddWidgetAsync(_fixture.SeedClient, pageId, "0.0", "image");

        await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
        var original = await EditorGrantsAsync();
        Assert.Contains("media", original);
        try
        {
            await SaveEditorGrantsAsync(original.Where(s => s != "media"));
            Assert.DoesNotContain("media", await EditorGrantsAsync());

            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);

            using (var picker = await GetAsync("/admin/media/picker"))
                Assert.Equal(HttpStatusCode.NotFound, picker.StatusCode);
            using (var preview = await GetAsync($"/admin/media/picker/preview?mediaId={sample.Id}"))
                Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);

            await CmsSeedHelpers.UpdateWidgetAsync(_fixture.SeedClient, pageId, "0.0", "image",
                [new("mediaId", sample.Id.ToString()), new("alt", "Typed by id")]);

            using var editor = await GetAsync($"/admin/pages/{pageId}/edit");
            Assert.Equal(HttpStatusCode.OK, editor.StatusCode);
            var html = await editor.Content.ReadAsStringAsync();
            Assert.DoesNotContain("data-media-picker", html);
            Assert.DoesNotContain("Open the media library in a new tab", html);
            Assert.Contains("name=\"props[mediaId]\"", html);
            Assert.Contains($"value=\"{sample.Id}\"", html);
        }
        finally
        {
            await AuthHelpers.ImpersonateAsAdminAsync(_fixture);
            await SaveEditorGrantsAsync(original);
            await CmsSeedHelpers.TryDeletePageAsync(_fixture.SeedClient, pageId);
            await AuthHelpers.ImpersonateAsEditorAsync(_fixture);
        }
    }
}
