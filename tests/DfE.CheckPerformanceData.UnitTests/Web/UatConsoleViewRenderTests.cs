using System;
using DfE.CheckPerformanceData.Web.Models.Dev;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Static Razor-source assertions for the UAT console view, mirroring ObservabilityViewRenderTests:
// read the .cshtml as text and assert on the markup contracts most likely to regress — the action
// buttons, the model-driven runner loop, the verdict controls, the coverage panel wiring, the
// surface launcher, the embedded board partial, and keyboard-operability (no onclick-only
// controls). Item-level content (titles, expect text, ids) is model-driven, so the runner loop is
// asserted here and the catalogue contents are pinned in UatCatalogTests.
public sealed class UatConsoleViewRenderTests
{
    private static string ReadView(string name, string folder = "DevUat")
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

        Assert.Contains("/dev/uat/drive?outcome=approved", view);
        Assert.Contains("/dev/uat/drive?outcome=rejected", view);
        Assert.Contains("/dev/uat/drive?outcome=scrutiny", view);
        Assert.Contains("/dev/uat/inject-failure", view);
        Assert.Contains("/dev/uat/seed-dlq", view);
    }

    [Fact]
    public void Index_HeadingIsDebugPipeline()
    {
        var view = ReadView("Index.cshtml");

        // The page H1 and title become "Debug Pipeline" (the nav label stays "Debug Pipelines").
        Assert.Contains(">Debug Pipeline</h1>", view);
        Assert.Contains("ViewData[\"Title\"] = \"Debug Pipeline\"", view);
    }

    [Fact]
    public void Index_GuidedRunnerMarkupIsGone()
    {
        var view = ReadView("Index.cshtml");

        // The 24-row guided pass/fail/skip checklist + its persistence wiring are removed. Assert
        // the verdict radios, the runner loop, the per-item notes and the progress/filter UI are
        // all absent so the runner can't silently creep back.
        Assert.DoesNotContain("data-uat-verdict", view);
        Assert.DoesNotContain("Model.Interactive", view);
        Assert.DoesNotContain("data-uat-notes", view);
        Assert.DoesNotContain("data-uat-progress", view);
        Assert.DoesNotContain("data-uat-filter", view);
        Assert.DoesNotContain("Guided UAT runner", view);
        Assert.DoesNotContain("value=\"pass\"", view);
        Assert.DoesNotContain("value=\"fail\"", view);
        Assert.DoesNotContain("value=\"skip\"", view);
    }

    [Fact]
    public void Index_NoLongerOffersClearOrExportPersistenceControls()
    {
        var view = ReadView("Index.cshtml");

        // The localStorage result store goes with the runner: no clear/export controls remain.
        Assert.DoesNotContain("data-uat-clear", view);
        Assert.DoesNotContain("data-uat-export", view);
    }

    [Fact]
    public void Index_RendersTheAutomatedCoveragePanelDrivenByTheModelAndManifest()
    {
        var view = ReadView("Index.cshtml");

        // The panel lists every automated id from the model and resolves status/filters client-side
        // from the served manifest + status files, with a copyable dotnet test command.
        Assert.Contains("Automated coverage", view);
        Assert.Contains("Model.AutomatedCoverageIds", view);
        Assert.Contains("data-coverage-url=\"/uat/uat-coverage.json\"", view);
        Assert.Contains("data-status-url=\"/uat/uat-status.json\"", view);
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
        Assert.Contains("uat-console.js", view);
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
    public void Index_KeepsTheCoveragePanelAndActionButtons()
    {
        var view = ReadView("Index.cshtml");

        // The automated-coverage panel and the drive/inject/seed action buttons survive the
        // guided-runner removal.
        Assert.Contains("data-uat-coverage", view);
        Assert.Contains("/dev/uat/drive?outcome=approved", view);
        Assert.Contains("/dev/uat/inject-failure", view);
        Assert.Contains("/dev/uat/seed-dlq", view);
    }

    [Fact]
    public void Index_EveryActionButtonCarriesATooltipTitle()
    {
        var view = ReadView("Index.cshtml");

        // Accessible hover help: each action button has a title attribute (the data-uat-tip hooks
        // mark the buttons the page wires tooltips to). At least the three drive presets, inject,
        // seed and the board explainer are covered.
        Assert.Contains("title=", view);
        Assert.Contains("data-uat-tip", view);
    }

    [Fact]
    public void Index_ExplainsTheAnimatedWorkflowBoard()
    {
        var view = ReadView("Index.cshtml");

        // A concise explainer on the board section: what it shows and how to read the envelopes.
        Assert.Contains("envelope", view, StringComparison.OrdinalIgnoreCase);
    }
}
