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
    private static string DemoPanel() => WebFile("Views", "Observability", "_DemoPanel.cshtml");
    private static string BoardJs() => WebFile("wwwroot", "js", "observability-board.js");
    private static string UatConsoleJs() => WebFile("wwwroot", "js", "uat-console.js");
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

    // --- Round 3: Pause moved into the Demo panel (only shown while Demo is expanded) and is a
    //     clear primary button, not the old grey-on-grey board toolbar ---

    [Fact]
    public void Pause_LivesInTheDemoPanel_NotTheBoardToolbar()
    {
        var board = Board();
        var panel = DemoPanel();

        // Per UAT round 3, Pause should only show when the Demo panel is expanded, so it moved out of
        // the always-on board toolbar into the Demo panel and is styled as a prominent button.
        Assert.Contains("data-obs-pause", panel);
        Assert.Contains("aria-pressed", panel);
        Assert.Contains("obs-demo-panel__pause", panel);
        // The board no longer carries its own pause toolbar.
        Assert.DoesNotContain("data-obs-pause", board);
        Assert.DoesNotContain("obs-board__toolbar", board);
    }

    [Fact]
    public void BoardJs_PauseFreezesMotion_AndIsResolvedAtDocumentLevel()
    {
        var js = BoardJs();

        // The engine exposes a pause toggle that freezes envelope motion and the replay/trickle; the
        // control is resolved at document level so it works from the Demo panel.
        Assert.Contains("togglePause", js);
        Assert.Contains("data-obs-pause", js);
    }

    // --- Round 3: one envelope per MESSAGE (keyed by reference), not one per stage row ---

    [Fact]
    public void BoardJs_AnimatesOneEnvelopePerMessage_KeyedByReference()
    {
        var js = BoardJs();

        // The engine remembers which references it has animated and primes a baseline on the first
        // snapshot so history is not replayed on load — so one drive shows exactly one envelope.
        Assert.Contains("animatedRefs", js);
        Assert.Contains("primed", js);
        // Optimistic, correctly-routed feedback for an AJAX drive, deduped against the SSE rows.
        Assert.Contains("presentDrive", js);
    }

    [Fact]
    public void BoardJs_DecidedEnvelopeRoutesThroughStatusThenZendeskQueueThenTicket()
    {
        var js = BoardJs();

        // Canonical flow: the decision box feeds the Zendesk queue, which feeds the ticket — the
        // engine never sends a decided envelope straight from its status to the ticket.
        Assert.Contains("stage:zendesk-queue", js);
        Assert.Contains("decisionAnchor", js);
    }

    [Fact]
    public void BoardJs_ColoursTheEnvelopeByDecision()
    {
        var js = BoardJs();

        // The envelope fill reflects the outcome: rejected red, scrutiny yellow (approved blue).
        Assert.Contains("DECISION_COLOURS", js);
        Assert.Contains("#ffdd00", js); // Scrutiny yellow
        Assert.Contains("#d4351c", js); // Rejected / failed red
    }

    [Fact]
    public void BoardJs_SingleStepIsAStickyMode_NotASelfUntickingButton()
    {
        var js = BoardJs();

        // Single step is a sticky mode like slow motion (it must stay ticked); both compose via a
        // speed product.
        Assert.Contains("setStepMode", js);
        Assert.Contains("stepFactor", js);
    }

    [Fact]
    public void BoardJs_DemoTrickleSpreadsAcrossAllOutcomeTypes()
    {
        var js = BoardJs();

        // Trickle/single-step send a random outcome so the board shows a realistic spread, not only
        // the happy ticket path.
        Assert.Contains("randomOutcome", js);
        Assert.Contains("AutoRejected", js);
    }

    [Fact]
    public void UatConsole_DriveGivesOptimisticBoardFeedbackByOutcome()
    {
        var js = UatConsoleJs();

        // The AJAX drive tells the board the new reference and the intended outcome so it shows one
        // correctly-routed envelope immediately (no fake extra envelope, no full reload).
        Assert.Contains("present", js);
        Assert.Contains("outcomeForForm", js);
    }

    // --- Round 3: the pipeline-state section is a live MATRIX grid (rows = messages, columns =
    //     stages), replacing the old summary list + recent-transitions list ---

    [Fact]
    public void Board_PipelineStateIsAMatrixGrid_NotTheOldSummaryAndTransitionsList()
    {
        var board = Board();

        // A scrollable table the engine fills, newest message at the top.
        Assert.Contains("data-obs-grid", board);
        Assert.Contains("Recent messages", board);
        Assert.Contains("obs-board__grid", board);
        // Stage columns; the queue headers link to the queue pages in a new tab.
        Assert.Contains("Zendesk ticket", board);
        Assert.Contains("/admin/queues/list/rules-engine", board);
        Assert.Contains("/admin/queues/list/zendesk", board);
        Assert.Contains("target=\"_blank\"", board);
        // The old summary list + recent-transitions live region are gone.
        Assert.DoesNotContain("Recent transitions", board);
        Assert.DoesNotContain("data-obs-transitions", board);
    }

    [Fact]
    public void BoardJs_AccumulatesPerMessageRowsAndRendersTheMatrix()
    {
        var js = BoardJs();

        // Per-reference rows are accumulated from the recorded stage events and rendered into the
        // grid body; the queue waits are derived between the stage timestamps.
        Assert.Contains("ingestEvent", js);
        Assert.Contains("renderGrid", js);
        Assert.Contains("data-obs-grid", js);
    }

    // --- Round 3: the headline tiles update live from the SSE snapshot, not only on a refresh ---

    [Fact]
    public void BoardJs_KeepsHeadlineTilesLive()
    {
        var js = BoardJs();

        // The depth tile tracks the summed queue depths each snapshot; the processed-today tile
        // ticks up as each message is processed through to a ticket.
        Assert.Contains("data-obs-tile-processed", js);
        Assert.Contains("data-obs-tile-depth", js);
        Assert.Contains("bumpProcessed", js);
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
