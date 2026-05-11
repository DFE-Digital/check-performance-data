using System.Diagnostics;
using System.Net;

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
