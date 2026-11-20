using System.Net;
using System.Text.Json;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// Puts a sample image in the media library through the dev-only /dev/media/sample endpoint,
// which runs the real upload pipeline. Each kind is one fixed file, so seeding again returns the
// same image and runs leave nothing behind. An alt text, when given, is set on the image. Throws when the endpoint is not there, rather than
// letting a test pass without an image.
public static class MediaSeedHelpers
{
    public sealed record SeededMedia(Guid Id, int Version, string Url, string SrcSet);

    public static async Task<SeededMedia> SeedSampleAsync(TestHttpClient client, string kind, string? alt = null)
    {
        var (token, cookie) = await AntiforgeryHelpers.ScrapeAsync(client, "/dev/antiforgery-token");
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(client.BaseAddress!, $"/dev/media/sample?kind={kind}" + (alt is null ? "" : "&alt=" + Uri.EscapeDataString(alt))))
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]),
        };
        req.Headers.Add("Cookie", cookie);

        using var response = await client.SendAsync(req);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(
                $"POST /dev/media/sample returned {(int)response.StatusCode}. Body head: {body[..Math.Min(400, body.Length)]}");

        return JsonSerializer.Deserialize<SeededMedia>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("POST /dev/media/sample returned no body.");
    }
}
