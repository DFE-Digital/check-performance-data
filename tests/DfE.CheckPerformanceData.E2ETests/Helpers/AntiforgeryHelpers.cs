using System.Net;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class AntiforgeryHelpers
{
    private const string AntiforgeryCookiePrefix = ".AspNetCore.Antiforgery.";
    private const string TokenInputSelector = "input[name='__RequestVerificationToken']";

    private const int MaxAttempts = 3;
    private const int RetryDelayMilliseconds = 1000;

    private static readonly HtmlParser Parser = new();

    public static async Task<(string Token, string Cookie)> ScrapeAsync(HttpClient client, string formPath)
    {
        if (client.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "AntiforgeryHelpers.ScrapeAsync requires the supplied HttpClient to have a BaseAddress.");
        }

        // One-shot GETs at fixture start-up race an app that is barely request-ready. Each form
        // POST in the suite depends on this token+cookie pair, so a transient failure here fails
        // the caller, not the test that would otherwise 400 on the antiforgery validation.
        // Bounded, short-delay retry; the token is fetched afresh each attempt.
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await ScrapeOnceAsync(client, formPath);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                lastError = ex;
                if (attempt == MaxAttempts)
                {
                    break;
                }

                await Task.Delay(RetryDelayMilliseconds);
            }
        }

        throw new InvalidOperationException(
            $"Antiforgery token not obtained from {formPath} after {MaxAttempts} attempts.",
            lastError);
    }

    private static async Task<(string Token, string Cookie)> ScrapeOnceAsync(HttpClient client, string formPath)
    {
        var cookieContainer = new CookieContainer();

        // /dev/antiforgery-token only issues the token+cookie pair for editor-role users,
        // so seed the dev impersonation cookie (set fixture-wide by AuthHelpers) into the
        // container before the GET. Adding a Cookie: header manually doesn't work when
        // UseCookies=true — the handler strips it and substitutes its own container.
        var impersonation = TestHttpClients.ImpersonationCookieHeader;
        if (!string.IsNullOrEmpty(impersonation))
        {
            var equalsIndex = impersonation.IndexOf('=');
            if (equalsIndex > 0)
            {
                var name = impersonation[..equalsIndex];
                var value = impersonation[(equalsIndex + 1)..];
                cookieContainer.Add(client.BaseAddress, new Cookie(name, value) { Path = "/" });
            }
        }

        using var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
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
