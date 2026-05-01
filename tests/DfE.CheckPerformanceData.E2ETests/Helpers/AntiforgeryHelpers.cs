using System.Net;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class AntiforgeryHelpers
{
    private static readonly Regex AntiforgeryCookieRegex =
        new(@"^\.AspNetCore\.Antiforgery\.", RegexOptions.Compiled);

    private static readonly Regex AntiforgeryTokenRegex =
        new(
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task<(string Token, string Cookie)> ScrapeAsync(HttpClient client, string formPath)
    {
        if (client.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "AntiforgeryHelpers.ScrapeAsync requires the supplied HttpClient to have a BaseAddress.");
        }

        using var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = false
        };

        using var scrapeClient = new HttpClient(handler)
        {
            BaseAddress = client.BaseAddress
        };

        var response = await scrapeClient.GetAsync(formPath);
        response.EnsureSuccessStatusCode();

        string? cookieHeader = null;
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                var nameValue = setCookie.Split(';', 2)[0].Trim();
                var equalsIndex = nameValue.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var cookieName = nameValue[..equalsIndex];
                if (AntiforgeryCookieRegex.IsMatch(cookieName))
                {
                    cookieHeader = nameValue;
                    break;
                }
            }
        }

        var body = await response.Content.ReadAsStringAsync();
        var match = AntiforgeryTokenRegex.Match(body);
        var token = match.Success
            ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            : null;

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(cookieHeader))
        {
            throw new InvalidOperationException(
                $"Antiforgery token not found in form at {formPath}");
        }

        return (token, cookieHeader);
    }
}
