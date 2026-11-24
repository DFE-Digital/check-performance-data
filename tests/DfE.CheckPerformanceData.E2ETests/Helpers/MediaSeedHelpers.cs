using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// Media library seeding and clean-up over HTTP.
//
// SeedSampleAsync puts a sample image in the library through the dev-only /dev/media/sample
// endpoint, which runs the real upload pipeline. Each kind is one fixed file, so seeding again
// returns the same image. Other tests rely on the samples, so no test edits, replaces or deletes them.
//
// UploadAsync, DeleteAsync and DeleteMatchingAsync use the library's own admin endpoints, so the
// signed-in user must hold the media section (impersonate admin first). Tests that upload delete
// their images again, by id or by their unique e2e-{guid} names.
//
// AddUsageAsync and ClearUsagesAsync record or remove a page's use of an image through the
// dev-only /dev/media/usage endpoints, until pages record their images themselves.
//
// Each helper throws when its endpoint is not there, rather than letting a test pass without data.
public static class MediaSeedHelpers
{
    public sealed record SeededMedia(Guid Id, int Version, string Url, string SrcSet);

    private const string LibraryPath = "/admin/media";

    private static readonly Regex DetailLink = new("href=\"/admin/media/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\"", RegexOptions.Compiled);

    public static async Task<SeededMedia> SeedSampleAsync(TestHttpClient client, string kind, string? alt = null)
    {
        var body = await PostDevAsync(client, $"/dev/media/sample?kind={kind}" + (alt is null ? "" : "&alt=" + Uri.EscapeDataString(alt)));
        return JsonSerializer.Deserialize<SeededMedia>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("POST /dev/media/sample returned no body.");
    }

    // Uploads one file the way the library page's script does, and returns the new image's id.
    public static async Task<Guid> UploadAsync(TestHttpClient client, string fileName, byte[] bytes, string contentType = "image/png")
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, LibraryPath);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, $"{LibraryPath}/upload"))
        {
            Content = new MultipartFormDataContent { { file, "files", fileName } },
        };
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-XSRF-TOKEN", token);
        request.Headers.Add("Cookie", cookie);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(body, "\"detailUrl\":\"/admin/media/([0-9a-f-]{36})\"");
        if (response.StatusCode != HttpStatusCode.OK || !body.Contains("\"outcomeName\":\"created\"") || !match.Success)
            throw new InvalidOperationException(
                $"POST {LibraryPath}/upload returned {(int)response.StatusCode}. Body head: {body[..Math.Min(400, body.Length)]}");
        return Guid.Parse(match.Groups[1].Value);
    }

    // Posts the image page's replace form with a new file. The library redirects to the image page
    // when the new version is stored.
    public static async Task ReplaceAsync(TestHttpClient client, Guid id, string fileName, byte[] bytes, string contentType = "image/png")
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, LibraryPath);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, $"{LibraryPath}/{id:D}/replace"))
        {
            Content = new MultipartFormDataContent
            {
                { new StringContent(token), "__RequestVerificationToken" },
                { file, "file", fileName },
            },
        };
        request.Headers.Add("Cookie", cookie);

        using var response = await client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.Found && response.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"POST {LibraryPath}/{{id}}/replace returned {(int)response.StatusCode}. Body head: {body[..Math.Min(400, body.Length)]}");
        }
    }

    // Posts the library's own delete form. Returns the status: 302 when deleted, 409 when a page
    // uses the image, 404 when it is not there.
    public static async Task<HttpStatusCode> DeleteAsync(TestHttpClient client, Guid id)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, LibraryPath);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, $"{LibraryPath}/{id:D}/delete"))
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]),
        };
        request.Headers.Add("Cookie", cookie);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    // Deletes every image whose name, alt text or tag contains the term. Use a unique e2e-{guid}
    // term only, so a test never removes another test's images.
    public static async Task DeleteMatchingAsync(TestHttpClient client, string term)
    {
        if (!term.StartsWith("e2e-", StringComparison.Ordinal))
            throw new ArgumentException("Delete only images named with a unique e2e- prefix.", nameof(term));

        using var list = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get,
            new Uri(client.BaseAddress!, $"{LibraryPath}?search={Uri.EscapeDataString(term)}")));
        var html = await list.Content.ReadAsStringAsync();
        foreach (var id in DetailLink.Matches(html).Select(m => Guid.Parse(m.Groups[1].Value)).Distinct())
            await DeleteAsync(client, id);
    }

    public static Task AddUsageAsync(TestHttpClient client, Guid mediaId, Guid pageId) =>
        PostDevAsync(client, $"/dev/media/usage?mediaId={mediaId:D}&pageId={pageId:D}");

    public static Task ClearUsagesAsync(TestHttpClient client, Guid mediaId) =>
        PostDevAsync(client, $"/dev/media/usage/clear?mediaId={mediaId:D}");

    private static async Task<string> PostDevAsync(TestHttpClient client, string pathAndQuery)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, pathAndQuery))
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]),
        };
        req.Headers.Add("Cookie", cookie);

        using var response = await client.SendAsync(req);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(
                $"POST {pathAndQuery.Split('?')[0]} returned {(int)response.StatusCode}. Body head: {body[..Math.Min(400, body.Length)]}");
        return body;
    }
}
