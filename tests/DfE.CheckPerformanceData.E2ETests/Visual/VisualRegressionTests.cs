using System.Runtime.InteropServices;
using DfE.CheckPerformanceData.E2ETests.Fixtures;
using DfE.CheckPerformanceData.E2ETests.Helpers;
using Microsoft.Playwright;

namespace DfE.CheckPerformanceData.E2ETests.Visual;

[Collection("E2E")]
[Trait("Category", "W4")]
[Trait("Category", "VisualRegression")]
public sealed class VisualRegressionTests(PlaywrightFixture fixture) : SeedingPageTest(fixture)
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

    private string _warningSlug = "";

    public override BrowserNewContextOptions ContextOptions() =>
        new() { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } };

    protected override async Task SeedAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Snapshots are Linux-only; skip the seed work on Windows/macOS so dev runs are fast.
            return;
        }

        var (_, slug) = await SeedHelpers.SeedWikiPageReturningSlugAsync(
            Fixture.SeedClient,
            title: "warning-vr",
            body: WarningTextBody,
            parentId: null,
            TrackedIds);

        _warningSlug = slug;
    }

    // --- HelpPageMatchesSnapshot ---

    [SkippableFact]
    public async Task HelpPageMatchesSnapshot()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Visual regression Linux-only");

        await Page.GotoAsync($"{Fixture.BaseUrl}/help");
        await Page.StabiliseAsync();

        await Page.MatchSnapshotAsync("help-page.png");
    }

    // --- WarningTextPageMatchesSnapshot ---

    [SkippableFact]
    public async Task WarningTextPageMatchesSnapshot()
    {
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            "Visual regression Linux-only");

        await Page.GotoAsync($"{Fixture.BaseUrl}/help/{_warningSlug}");
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

        // Collect any snapshots written on first-run instead of letting the helper throw
        // mid-sweep. Without the accumulator the test would abort on the first missing
        // viewport and require N+1 CI runs to bootstrap; with it, every viewport is
        // generated in a single run and the test fails once at the end.
        var createdSnapshots = new List<string>();

        foreach (var (label, width, height) in viewports)
        {
            await Page.SetViewportSizeAsync(width, height);
            await Page.GotoAsync($"{Fixture.BaseUrl}/help");
            await Page.StabiliseAsync();

            await Page.MatchSnapshotAsync(
                $"help-page-{label}-{width}x{height}.png",
                createdSnapshots: createdSnapshots);
        }

        if (createdSnapshots.Count > 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"Bootstrapped {createdSnapshots.Count} snapshot(s): "
                + $"{string.Join(", ", createdSnapshots)} — commit and re-run to verify.");
        }
    }
}
