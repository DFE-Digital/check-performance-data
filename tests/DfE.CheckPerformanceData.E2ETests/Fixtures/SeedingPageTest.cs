using System.IO;
using System.Threading;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Fixtures;

// PageTest already implements IAsyncLifetime; redeclaring it here re-establishes
// the C# interface map at this class so xUnit dispatches our `new` InitializeAsync
// and DisposeAsync (which run SeedAsync + cleanup) instead of PageTest's. Without
// the explicit `: IAsyncLifetime`, the `new` methods would only be visible on the
// static type — interface invocation would land on PageTest's base methods and
// silently skip seeding. Don't remove the redeclaration.
public abstract class SeedingPageTest(PlaywrightFixture fixture) : PageTest, IAsyncLifetime
{
    protected PlaywrightFixture Fixture { get; } = fixture;

    // When CPD_E2E_TRACES_DIR is set, a Playwright trace of the failing test is written
    // there (one {ClassName}.{ordinal}.zip per failed test; passing tests stop tracing
    // without a path and discard). Drives CI's e2e-traces artifact — see E2E-Robustness.
    private static readonly string? TracesDirectory = ReadTracesDirectory();

    private static int _traceOrdinal;

    public new virtual async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // A freshly deployed review pod is cold — the first handful of navigations can
        // take tens of seconds while the pipeline JITs and the first EF query runs.
        // Playwright's 30s navigation default turns a slow-but-healthy first load into
        // a test failure; give the suite a wider one. The fixture's warm-up above means
        // steady-state navigations should still be fast, so the README's "investigate
        // any single test above 30s" rule keeps working.
        Page.SetDefaultNavigationTimeout(60000);

        if (TracesDirectory is not null)
        {
            await Context.Tracing.StartAsync(new TracingStartOptions
            {
                Name = GetType().Name,
                Screenshots = true,
                Snapshots = true
            });
        }

        // Mirror the fixture-level impersonation cookie into the Playwright browser
        // context so Page.GotoAsync(...) requests authenticate as editor. Without this
        // the seed HttpClient is impersonating but the headless Chromium that drives
        // the assertions isn't, and any editor-gated route (/content-block/versions/{key},
        // /admin/*, etc.) 302s to DfE Sign-In during the test.
        var impersonation = Fixture.SeedClient.ImpersonationCookieHeader;
        if (!string.IsNullOrEmpty(impersonation))
        {
            var equalsIndex = impersonation.IndexOf('=');
            if (equalsIndex > 0)
            {
                await Context.AddCookiesAsync([new Cookie
                {
                    Name = impersonation[..equalsIndex],
                    Value = impersonation[(equalsIndex + 1)..],
                    Url = Fixture.BaseUrl
                }]);
            }
        }

        await SeedAsync();
    }

    public new virtual async Task DisposeAsync()
    {
        // Stop tracing BEFORE base.DisposeAsync: on success the base disposes the
        // BrowserContext (so StopAsync would find nothing to read), and on failure the
        // test's trace is the whole point. TestOk distinguishes the two — the inherited
        // ExceptionCapturer flips it on any FirstChanceException during the test. A
        // failed trace-save must never mask the test's real failure, so every tracing
        // call here is best-effort.
        if (TracesDirectory is not null)
        {
            try
            {
                var path = TestOk
                    ? (TracingStopOptions?)null
                    : new TracingStopOptions
                    {
                        Path = Path.Combine(
                            TracesDirectory,
                            $"{GetType().Name}.{Interlocked.Increment(ref _traceOrdinal)}.zip")
                    };
                await Context.Tracing.StopAsync(path);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine(
                    $"[e2e] {GetType().Name}: failed to save trace ({ex.GetType().Name}: {ex.Message})");
            }
        }

        await base.DisposeAsync();
    }

    private static string? ReadTracesDirectory()
    {
        var raw = Environment.GetEnvironmentVariable("CPD_E2E_TRACES_DIR");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return Path.GetFullPath(raw);
    }

    // Override to seed per-test data. Default: no-op.
    protected virtual Task SeedAsync() => Task.CompletedTask;
}
