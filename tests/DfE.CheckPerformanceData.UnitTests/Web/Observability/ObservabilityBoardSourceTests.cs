namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Observability;

// Source-text assertions for the workflow board: the _Board.cshtml skeleton, the
// observability-board.js animation engine and observability.css. Mirrors
// ObservabilityViewRenderTests' hostless disk-read style — no JS test runner is wired, so the
// board engine's contracts are pinned by asserting on its source, the same way the Razor and CSS
// contracts are. Covers the round-2 board feedback: the "Zendesk ticket" rename, per-stage dwell
// variance, and the red failure envelope.
public sealed class ObservabilityBoardSourceTests
{
    private static string RepoRoot
    {
        get
        {
            var thisFile = ThisFilePath();
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", ".."));
        }
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static string WebFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { RepoRoot, "src", "DfE.CheckPerformanceData.Web" }.Concat(parts).ToArray()));

    private static string Board() => WebFile("Views", "Observability", "_Board.cshtml");
    private static string BoardJs() => WebFile("wwwroot", "js", "observability-board.js");
    private static string Css() => WebFile("wwwroot", "css", "observability.css");

    // --- Item 4: the final board box is labelled "Zendesk ticket", not "Ticket" ---

    [Fact]
    public void Board_FinalStageLabel_IsZendeskTicket()
    {
        var board = Board();

        Assert.Contains("Zendesk ticket", board);
        // The bare "Ticket" label is gone (the stage key stays "ticket"; only the display label changes).
        Assert.DoesNotContain("Label = \"Ticket\"", board);
    }

    // --- Item 1: per-stage dwell varies, so envelopes desync rather than moving in lockstep ---

    [Fact]
    public void BoardJs_DefinesPerStageDwell_NotASingleSharedConstant()
    {
        var js = BoardJs();

        // A per-stage dwell map keyed by stage so each box holds an envelope for its own duration.
        Assert.Contains("STAGE_DWELL_BY_KEY", js);
        // And the walk reads the dwell for the specific stage it is leaving, not one global value.
        Assert.Contains("dwellFor", js);
    }

    // --- Item 2: the injected/failed envelope renders in GDS red, distinct from the blue good ones ---

    [Fact]
    public void BoardJs_FailedEnvelope_CarriesAFailureClass()
    {
        var js = BoardJs();

        // The token element is marked as a failure so CSS can paint it red; the SVG fill is the GDS
        // red rather than the blue used for good messages.
        Assert.Contains("obs-board__token--failed", js);
        Assert.Contains("#d4351c", js);
    }

    [Fact]
    public void Css_FailedToken_IsGdsRed()
    {
        var css = Css();

        Assert.Contains(".obs-board__token--failed", css);
        Assert.Contains("#d4351c", css);
    }
}
