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

        // Per the two-pass bootstrap behaviour in MatchSnapshotAsync, a first-run write
        // is success — every missing viewport gets written in this single pass and the
        // test passes. The next run does the real comparison. The accumulator still
        // surfaces *which* viewports were freshly bootstrapped, for diagnostics only.
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
            Console.WriteLine(
                $"Bootstrapped {createdSnapshots.Count} viewport snapshot(s): "
                + $"{string.Join(", ", createdSnapshots)}. Next run will compare.");
        }
    }
}
