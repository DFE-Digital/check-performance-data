using System.Net;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class AntiforgeryHelpers
{
    private const string AntiforgeryCookiePrefix = ".AspNetCore.Antiforgery.";
    private const string TokenInputSelector = "input[name='__RequestVerificationToken']";

    private static readonly HtmlParser Parser = new();

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
                if (cookieName.StartsWith(AntiforgeryCookiePrefix, StringComparison.Ordinal))
                {
                    cookieHeader = nameValue;
                    break;
                }
            }
        }

        var body = await response.Content.ReadAsStringAsync();
        var document = await Parser.ParseDocumentAsync(body);
        var token = (document.QuerySelector(TokenInputSelector) as IHtmlInputElement)?.Value;

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(cookieHeader))
        {
            throw new InvalidOperationException(
                $"Antiforgery token not found in form at {formPath}");
        }

        return (token, cookieHeader);
    }
}
