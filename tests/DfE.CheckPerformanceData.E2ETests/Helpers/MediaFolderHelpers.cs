using System.Net;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// Media library folders over HTTP, through the library's own forms: create a folder, find its id
// in its parent's folder list, upload an image into it, and delete it. Folder names in tests
// carry a unique e2e-{guid} prefix.
public static class MediaFolderHelpers
{
    private const string LibraryPath = "/admin/media";

    public static async Task<Guid> CreateAsync(TestHttpClient client, string name, Guid? parent = null)
    {
        if (!name.StartsWith("e2e-", StringComparison.Ordinal))
            throw new ArgumentException("Name test folders with a unique e2e- prefix.", nameof(name));

        using var response = await PostAsync(client, $"{LibraryPath}/folders",
            parent is Guid p ? [new("name", name), new("parent", p.ToString())] : [new("name", name)]);
        if (response.StatusCode != HttpStatusCode.Found && response.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException($"POST {LibraryPath}/folders returned {(int)response.StatusCode}.");

        var listUrl = parent is Guid id ? $"{LibraryPath}?folder={id:D}" : LibraryPath;
        using var list = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(client.BaseAddress!, listUrl)));
        var html = await list.Content.ReadAsStringAsync();
        var match = Regex.Match(html, $"data-media-folder=\"([0-9a-f-]{{36}})\"><a [^>]*>{Regex.Escape(name)}</a>");
        if (!match.Success) throw new InvalidOperationException($"Folder {name} is not listed in {listUrl}.");
        return Guid.Parse(match.Groups[1].Value);
    }

    // 302 when deleted, 409 when the folder holds an image or a folder, 404 when it is not there.
    public static async Task<HttpStatusCode> DeleteAsync(TestHttpClient client, Guid id)
    {
        using var response = await PostAsync(client, $"{LibraryPath}/folders/{id:D}/delete", []);
        return response.StatusCode;
    }

    // 302 when moved; 400 with the page that says why not.
    public static async Task<(HttpStatusCode Status, string Body)> MoveAsync(TestHttpClient client, Guid id, string parent)
    {
        using var response = await PostAsync(client, $"{LibraryPath}/folders/{id:D}/move", [new("parent", parent)]);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public static async Task<Guid> UploadIntoAsync(TestHttpClient client, Guid? folder, string fileName, byte[] bytes)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, LibraryPath);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var path = folder is Guid f ? $"{LibraryPath}/upload?folder={f:D}" : $"{LibraryPath}/upload";
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, path))
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
            throw new InvalidOperationException($"POST {path} returned {(int)response.StatusCode}. Body head: {body[..Math.Min(300, body.Length)]}");
        return Guid.Parse(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostAsync(TestHttpClient client, string path, List<KeyValuePair<string, string>> fields)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, LibraryPath);
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, path))
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token), .. fields]),
        };
        request.Headers.Add("Cookie", cookie);
        return await client.SendAsync(request);
    }
}
