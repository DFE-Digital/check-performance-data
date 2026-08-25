namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Observability;

// Source-text assertions for the workflow board: the _Board.cshtml skeleton, the
// observability-board.js animation engine and observability.css. Mirrors
// ObservabilityViewRenderTests' hostless disk-read style — no JS test runner is wired, so the
// board engine's contracts are pinned by asserting on its source, the same way the Razor and CSS
// contracts are. Covers the "Zendesk ticket" rename, per-stage dwell variance, and the red
// failure envelope.
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

    // --- A destination row of decision boxes sits with the dead-letter marker after the engine ---

    [Fact]
    public void Board_RendersDecisionDestinationBoxes_OnTheDeadLetterRow()
    {
        var board = Board();

        // Three decision destinations, each keyed by its decisionStatus value, alongside the
        // existing Dead-letter queue, all on one destination row beneath the lane. The keys are
        // declared in the view's decisions array (rendered into each box's data-obs-decision).
        Assert.Contains("\"AutoApproved\"", board);
        Assert.Contains("\"AutoRejected\"", board);
        Assert.Contains("\"Scrutiny\"", board);
        Assert.Contains("Dead-letter queue", board);

        // Each decision box reuses the DLQ-marker structure, keyed via data-obs-decision so the
        // engine can anchor to it and toggle its active state as envelopes land.
        Assert.Contains("data-obs-decision=\"@decision.Key\"", board);

        // The four destinations share a single row container.
        Assert.Contains("obs-board__destinations", board);
    }

    [Fact]
    public void BoardJs_RoutesEnvelopesToTheirDecisionBox()
    {
        var js = BoardJs();

        // A decisionAnchor sits alongside dlqAnchor, mapping a decisionStatus to its box centre, so a
        // decided envelope routes into the matching destination box before continuing to the ticket.
        Assert.Contains("decisionAnchor", js);
        // Decision boxes light up as envelopes land, like the DLQ marker's active attribute.
        Assert.Contains("data-obs-decision-active", js);
    }

    // --- Multiple envelopes at one box stack diagonally, with a >4 count overlay ---

    [Fact]
    public void BoardJs_StacksEnvelopesAndShowsACountOverlayWhenCrowded()
    {
        var js = BoardJs();

        // Per-anchor occupancy is tracked so envelopes at the same box offset diagonally rather than
        // overlapping exactly, and a numeric overlay replaces the pile beyond a threshold.
        Assert.Contains("occupancy", js);
        Assert.Contains("STACK_OFFSET", js);
        Assert.Contains("STACK_MAX_VISIBLE", js);
        Assert.Contains("obs-board__stack-count", js);
    }

    [Fact]
    public void Css_StackCountOverlay_IsStyled()
    {
        var css = Css();

        Assert.Contains(".obs-board__stack-count", css);
    }

    // --- An always-available Pause control + hover/focus message details ---

    [Fact]
    public void Board_CarriesAnAlwaysAvailablePauseControl()
    {
        var board = Board();

        // Pause lives on the board itself (not the dev-only Demo panel), so it is reachable without
        // the Demo panel. It carries the hook the engine binds and an accessible toggle state.
        Assert.Contains("data-obs-pause", board);
        Assert.Contains("aria-pressed", board);
    }

    [Fact]
    public void BoardJs_PauseFreezesMotion_AndIsReachableFromTheBoard()
    {
        var js = BoardJs();

        // The engine exposes a pause toggle that freezes envelope motion and the replay/trickle, and
        // the board-level control is wired to it (resolved from the board root, not just the Demo panel).
        Assert.Contains("togglePause", js);
        Assert.Contains("data-obs-pause", js);
    }

    [Fact]
    public void BoardJs_SurfacesMessageDetailsOnHoverOrFocus()
    {
        var js = BoardJs();

        // Hover/focus reveals a positioned detail popover carrying the reference, stage, decision and
        // latency/time the envelope's aria-label already encodes.
        Assert.Contains("obs-board__token-detail", js);
    }

    [Fact]
    public void Css_TokenDetailPopover_IsStyled()
    {
        var css = Css();

        Assert.Contains(".obs-board__token-detail", css);
    }
}
