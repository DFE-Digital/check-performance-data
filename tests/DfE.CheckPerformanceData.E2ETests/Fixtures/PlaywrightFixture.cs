using System.Diagnostics;
using System.Net;
using DfE.CheckPerformanceData.E2ETests.Helpers;

namespace DfE.CheckPerformanceData.E2ETests.Fixtures;

public sealed class PlaywrightFixture : IAsyncLifetime
{
    private const int DefaultReadyTimeoutSeconds = 90;
    private const int PollIntervalMilliseconds = 2000;
    private const int SampleSeedAttempts = 3;
    private const int SampleSeedBackoffMilliseconds = 2000;

    public string BaseUrl { get; }

    public TestHttpClient SeedClient { get; }

    public PlaywrightFixture()
    {
        var configured = Environment.GetEnvironmentVariable("CPD_E2E_BASE_URL");
        var resolved = string.IsNullOrWhiteSpace(configured) ? "http://localhost:8080" : configured;
        BaseUrl = resolved.TrimEnd('/');

        SeedClient = new TestHttpClient
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

        // Run the seed so tests that depend on stable seeded content can rely on the rows
        // being present, whatever state the target environment's content is in. One button,
        // two seeds: sample pages are created where missing and otherwise left alone, while
        // the fixture pages this suite navigates to are re-imported over the top. The second
        // half is what matters here — a fixture whose versions an editor deleted still exists,
        // so a skip-on-collision seed walks past it and the route stays 404.
        await EnsureSamplePagesSeededAsync();
        await WarmUpRoutesAsync();
    }

    // The routes most tests land on share the coldest paths in the request pipeline
    // (DI-graph construction, the first EF query, the feature-flag/sample-pages store
    // fetch). Paying that cost during the first browser navigation eats into the
    // test's navigation timeout for no reason — pay it here instead, with the
    // impersonated editor (warm-up is best-effort: the readiness probe and the hard
    // seed gate above are the real health signals).
    private static readonly string[] WarmUpPaths = { "/", "/admin/pages" };

    private async Task WarmUpRoutesAsync()
    {
        foreach (var path in WarmUpPaths)
        {
            try
            {
                using var response = await SeedClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{path}"));
            }
            catch (HttpRequestException)
            {
                // Ignore — a warm-up failure is not a fixture failure.
            }
        }
    }

    private async Task EnsureSamplePagesSeededAsync()
    {
        // The sample-seed POST happens at fixture start-up, potentially before the app is fully
        // request-hardened. Bounded retry with a fresh antiforgery token per attempt so a
        // transient 5xx does not silently leak a missing-sample-page failure into the first
        // test that navigates to one. This is a hard gate: the seeded fixture pages are a
        // collection-wide precondition, so a seed that ultimately fails is a fixture failure
        // with the endpoint named in the message, not a later, worse-diagnosed test failure.
        Exception? lastError = null;
        for (var attempt = 1; attempt <= SampleSeedAttempts; attempt++)
        {
            try
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

                using var response = await SeedClient.SendAsync(request);
                // SampleSeed always redirects (302) to /admin/pages on success; a 5xx, or an
                // unauthenticated redirect away from that, means the seed did not run.
                if ((int)response.StatusCode is >= 200 and < 400)
                {
                    return;
                }

                lastError = new InvalidOperationException(
                    $"POST {BaseUrl}/admin/pages/sample-seed returned HTTP {(int)response.StatusCode} " +
                    $"(attempt {attempt}/{SampleSeedAttempts}).");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
            }

            await Task.Delay(SampleSeedBackoffMilliseconds);
        }

        throw new InvalidOperationException(
            $"Sample-page seeding failed after {SampleSeedAttempts} attempts. " +
            "Check POST /admin/pages/sample-seed is reachable (editor-gated) and the app has settled.",
            lastError);
    }

    public Task DisposeAsync()
    {
        SeedClient.Dispose();
        return Task.CompletedTask;
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
