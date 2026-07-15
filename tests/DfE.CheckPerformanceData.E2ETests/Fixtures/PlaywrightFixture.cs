using System.Diagnostics;
using System.Net;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Fixtures;

public sealed class PlaywrightFixture : IAsyncLifetime
{
    private const int DefaultReadyTimeoutSeconds = 90;
    private const int PollIntervalMilliseconds = 2000;

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

        // Impersonate as editor for the lifetime of the test collection so every
        // seed/CRUD endpoint sees an authenticated editor principal. Hits the dev-only
        // /dev/impersonate/editor route; in production the suite would 404 here and
        // every editor-gated test would fail, which is the desired outcome (E2E should
        // never run against prod).
        await AuthHelpers.ImpersonateAsEditorAsync(this);

        // Run the sample-page seeder so tests that depend on stable seeded content
        // (e.g. /wiki/wiki-sandbox for the back-to-top and deleted-content round trip
        // suites) can rely on the row being present on a fresh DB. Idempotent — the
        // seeder skips any (root, segment) whose path already exists, so re-running
        // is safe.
        await EnsureSamplePagesSeededAsync();
    }

    private async Task EnsureSamplePagesSeededAsync()
    {
        var (token, cookie) = await Helpers.AntiforgeryHelpers.ScrapeAsync(SeedClient, "/dev/antiforgery-token");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/admin/pages/sample-seed")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            }),
        };
        request.Headers.Add("Cookie", cookie);
        // 302 to /admin/pages on success (SampleSeed redirects); anything else means the
        // seed didn't run (unauthenticated, missing grant, endpoint moved). Best-effort:
        // downstream tests that need the seed will surface a specific missing-row failure.
        try
        {
            using var response = await Helpers.TestHttpClients.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            // Transport failures at fixture start-up are already surfaced by
            // WaitForDeploymentReadyAsync above.
        }
    }

    public async Task DisposeAsync()
    {
        // Final orphan sweep — catches any e2e-{Guid:N}-prefixed wiki page whose
        // creating test crashed before SeedingPageTest.DisposeAsync could soft-delete
        // it via tracked IDs. The slug prefix is the seed tag; everything that matches
        // is soft-deleted best-effort. Failures are swallowed so they can't mask the
        // suite outcome.
        try
        {
            await Helpers.SeedHelpers.SweepOrphanE2eWikiPagesAsync(SeedClient);
        }
        catch
        {
            // best-effort
        }

        SeedClient.Dispose();
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
