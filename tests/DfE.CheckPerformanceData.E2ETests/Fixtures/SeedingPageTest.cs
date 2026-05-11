using DfE.CheckPerformanceData.E2ETests.Helpers;
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

    // Wiki page ids accumulated by SeedAsync overrides. DisposeAsync soft-deletes
    // each one in reverse insertion order so child pages drop before their parents.
    protected List<int> TrackedIds { get; } = [];

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await SeedAsync();
    }

    public new async Task DisposeAsync()
    {
        foreach (var id in TrackedIds.AsEnumerable().Reverse())
        {
            try
            {
                await SeedHelpers.SoftDeleteWikiPageAsync(Fixture.SeedClient, id);
            }
            catch
            {
                // Best-effort cleanup; failure must not mask the test outcome.
            }
        }
        await base.DisposeAsync();
    }

    // Override to seed per-test data. Default: no-op.
    protected virtual Task SeedAsync() => Task.CompletedTask;
}
