using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace DfE.CheckPerformanceData.E2ETests.Visual;

[Collection("E2E")]
[Trait("Category", "W4")]
[Trait("Category", "VisualRegression")]
public sealed class VisualRegressionTests(PlaywrightFixture fixture) : PageTest, IAsyncLifetime
{
    private const string WarningTextBody = """
        <div class="govuk-warning-text">
          <span class="govuk-warning-text__icon" aria-hidden="true">!</span>
          <strong class="govuk-warning-text__text">
            <span class="govuk-visually-hidden">Warning</span>
            Important information about this page.
          </strong>
        </div>
        """;

    private readonly PlaywrightFixture _fixture = fixture;
    private readonly List<int> _trackedIds = [];
    private string _warningSlug = "";

    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } };

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Snapshots are Linux-only; skip the seed work on Windows/macOS so dev runs are fast.
            return;
        }

        var (_, slug) = await SeedHelpers.SeedWikiPageReturningSlugAsync(
            _fixture.SeedClient,
            title: "warning-vr",
            body: WarningTextBody,
            parentId: null,
            _trackedIds);

        _warningSlug = slug;
    }

    public new async Task DisposeAsync()
    {
        foreach (var id in _trackedIds.AsEnumerable().Reverse())
        {
            try
            {
                await SeedHelpers.SoftDeleteWikiPageAsync(_fixture.SeedClient, id);
            }
            catch
            {
                // Best-effort cleanup; cleanup failure must not mask test outcome.
            }
        }
        await base.DisposeAsync();
    }

    // --- HelpPageMatchesSnapshot ---

    [SkippableFact]
    public async Task HelpPageMatchesSnapshot()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Visual regression Linux-only");

        await Page.GotoAsync($"{_fixture.BaseUrl}/help");
        await Page.StabiliseAsync();

        await Page.MatchSnapshotAsync("help-page.png");
    }

    // --- WarningTextPageMatchesSnapshot ---

    [SkippableFact]
    public async Task WarningTextPageMatchesSnapshot()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Visual regression Linux-only");

        await Page.GotoAsync($"{_fixture.BaseUrl}/help/{_warningSlug}");
        await Page.StabiliseAsync();

        await Page.MatchSnapshotAsync("warning-text-page.png");
    }

    // --- MultiViewportSweep ---

    [SkippableFact]
    public async Task MultiViewportSweep()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Visual regression Linux-only");

        var viewports = new[]
        {
            ("desktop-large", 1920, 1080),
            ("mobile", 375, 667),
            ("tablet", 768, 1024),
        };

        foreach (var (label, width, height) in viewports)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{_fixture.BaseUrl}/help");
            await Page.StabiliseAsync();

            await Page.MatchSnapshotAsync($"help-page-{label}-{width}x{height}.png");
        }
    }
}
