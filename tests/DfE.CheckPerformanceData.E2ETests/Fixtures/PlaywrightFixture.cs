using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.E2ETests.Fixtures;

public sealed class PlaywrightFixture : IAsyncLifetime
{
    private const int DefaultReadyTimeoutSeconds = 90;
    private const int PollIntervalMilliseconds = 2000;

    private static readonly Regex AntiforgeryCookieRegex =
        new(@"^\.AspNetCore\.Antiforgery\.", RegexOptions.Compiled);

    private static readonly Regex AntiforgeryTokenRegex =
        new(
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string BaseUrl { get; }

    public HttpClient SeedClient { get; }

    public PlaywrightFixture()
    {
        var configured = Environment.GetEnvironmentVariable("CPD_E2E_BASE_URL");
        var resolved = string.IsNullOrWhiteSpace(configured) ? "http://localhost:8080" : configured;
        BaseUrl = resolved.TrimEnd('/');

        SeedClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl)
        };
    }

    public async Task InitializeAsync()
    {
        await WaitForDeploymentReadyAsync();
    }

    public Task DisposeAsync()
    {
        SeedClient.Dispose();
        return Task.CompletedTask;
    }

    public async Task<(string Token, string CookieHeader)> ScrapeAntiforgeryTokenAsync(string formPath)
    {
        using var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = false
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl)
        };

        var response = await client.GetAsync(formPath);
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

    private async Task WaitForDeploymentReadyAsync()
    {
        var timeoutSeconds = ReadTimeoutSeconds();
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                var response = await SeedClient.GetAsync("/healthcheck");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Deployment may not be reachable yet — count as not-ready and continue polling.
            }
            catch (TaskCanceledException)
            {
                // Request timed out — count as not-ready and continue polling.
            }

            if (stopwatch.Elapsed.TotalSeconds >= timeoutSeconds)
            {
                throw new InvalidOperationException(
                    $"E2E deployment not ready at {BaseUrl} after {timeoutSeconds}s");
            }

            await Task.Delay(PollIntervalMilliseconds);
        }
    }

    private static int ReadTimeoutSeconds()
    {
        var raw = Environment.GetEnvironmentVariable("CPD_E2E_READY_TIMEOUT_SECONDS");
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return DefaultReadyTimeoutSeconds;
    }
}
