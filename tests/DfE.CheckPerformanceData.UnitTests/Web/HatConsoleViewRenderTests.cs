using DfE.CheckPerformanceData.Web.Models.Dev;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Static Razor-source assertions for the HAT console view, mirroring ObservabilityViewRenderTests:
// read the .cshtml as text and assert on the markup contracts most likely to regress — the action
// buttons, the model-driven runner loop, the verdict controls, the coverage panel wiring, the
// surface launcher, the embedded board partial, and keyboard-operability (no onclick-only
// controls). Item-level content (titles, expect text, ids) is model-driven, so the runner loop is
// asserted here and the catalogue contents are pinned in HatCatalogTests.
public sealed class HatConsoleViewRenderTests
{
    private static string ReadView(string name, string folder = "DevHat")
    {
        var thisFile = ThisFilePath();
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        var view = Path.Combine(repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", folder, name);
        return File.ReadAllText(view);
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    [Fact]
    public void Index_RendersTheCoreDriveAndFailureActionButtons()
    {
        var view = ReadView("Index.cshtml");

        Assert.Contains("/dev/hat/drive?outcome=approved", view);
        Assert.Contains("/dev/hat/drive?outcome=rejected", view);
        Assert.Contains("/dev/hat/drive?outcome=scrutiny", view);
        Assert.Contains("/dev/hat/inject-failure", view);
        Assert.Contains("/dev/hat/seed-dlq", view);
    }

    [Fact]
    public void Index_RendersTheModelDrivenRunnerLoop()
    {
        var view = ReadView("Index.cshtml");

        // The runner is generated from the model, one row per interactive item, carrying the id,
        // title and expect text — not a hand-maintained list.
        Assert.Contains("Model.Interactive", view);
        Assert.Contains("data-hat-item=\"@item.Id\"", view);
        Assert.Contains("@item.Title", view);
        Assert.Contains("@item.Expected", view);
    }

    [Fact]
    public void Index_EachRunnerRowHasAPassFailSkipControlAndNotes()
    {
        var view = ReadView("Index.cshtml");

        // The verdict control is a real radio group keyed by item, persisted client-side.
        Assert.Contains("data-hat-verdict", view);
        Assert.Contains("value=\"pass\"", view);
        Assert.Contains("value=\"fail\"", view);
        Assert.Contains("value=\"skip\"", view);
        Assert.Contains("data-hat-notes", view);
    }

    [Fact]
    public void Index_RendersTheAutomatedCoveragePanelDrivenByTheModelAndManifest()
    {
        var view = ReadView("Index.cshtml");

        // The panel lists every automated id from the model and resolves status/filters client-side
        // from the served manifest + status files, with a copyable dotnet test command.
        Assert.Contains("Automated coverage", view);
        Assert.Contains("Model.AutomatedCoverageIds", view);
        Assert.Contains("data-coverage-url=\"/hat/hat-coverage.json\"", view);
        Assert.Contains("data-status-url=\"/hat/hat-status.json\"", view);
        Assert.Contains("dotnet test", view);
    }

    [Fact]
    public void Index_RendersTheSurfaceLauncherLinks()
    {
        var view = ReadView("Index.cshtml");

        Assert.Contains("/admin/observability", view);
        Assert.Contains("/admin/queues", view);
        Assert.Contains("/admin/queues/dlq", view);
        Assert.Contains("/dev/zendesk/outbox", view);
        Assert.Contains("/admin/share", view);
    }

    [Fact]
    public void Index_EmbedsTheObservabilityBoardPartial()
    {
        var view = ReadView("Index.cshtml");
        Assert.Contains("_Board", view);
    }

    [Fact]
    public void Index_PullsInTheConsoleScriptAndStylesheet()
    {
        var view = ReadView("Index.cshtml");
        Assert.Contains("hat-console.js", view);
        Assert.Contains("observability.css", view);
    }

    [Fact]
    public void Index_HasNoOnclickOnlyControlsForCoreContent()
    {
        var view = ReadView("Index.cshtml");
        // Core actions are forms/links/buttons, keyboard-reachable; no inline onclick handlers.
        Assert.DoesNotContain("onclick=", view);
    }

    [Fact]
    public void Index_OffersPhaseFilterClearAndExportControls()
    {
        var view = ReadView("Index.cshtml");
        Assert.Contains("data-hat-filter", view);
        Assert.Contains("data-hat-clear", view);
        Assert.Contains("data-hat-export", view);
    }
}
