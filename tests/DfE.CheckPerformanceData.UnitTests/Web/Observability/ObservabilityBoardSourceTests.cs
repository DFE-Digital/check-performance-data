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
    public void Board_OrdersDecisionBoxes_ApprovedScrutinyRejected()
    {
        var board = Board();

        // Per UAT the destination row reads Auto-approved → Scrutiny → Auto-rejected (then the
        // dead-letter box). The decisions array drives the layout order.
        var approved = board.IndexOf("\"AutoApproved\"", StringComparison.Ordinal);
        var scrutiny = board.IndexOf("\"Scrutiny\"", StringComparison.Ordinal);
        var rejected = board.IndexOf("\"AutoRejected\"", StringComparison.Ordinal);
        Assert.True(approved < scrutiny && scrutiny < rejected,
            $"expected Approved < Scrutiny < Rejected, got {approved}/{scrutiny}/{rejected}");
    }

    [Fact]
    public void Board_LinksTheTwoQueueBoxesToTheirAdminPages()
    {
        var board = Board();

        // The rules-queue and zendesk-queue boxes link to their queue admin pages (new tab). These
        // are the only queue links on the board — the matrix column headers are plain text.
        Assert.Contains("/admin/queues/list/rules-engine", board);
        Assert.Contains("/admin/queues/list/zendesk", board);
        Assert.Contains("obs-board__label govuk-link", board);
    }

    // --- A queue that drains to EMPTY must clear its waiting count and backing-up marker ---
    // Regression: an empty queue is ABSENT from the snapshot depths — the server groups existing
    // QueueMessages rows, so a queue with nothing waiting produces no group and no depth entry.
    // updateCounts therefore has to reset every known queue to empty BEFORE applying the depths the
    // snapshot does carry; otherwise a queue that went from N to 0 keeps its stale "N" badge and
    // orange backing-up state forever, because the per-depth loop never visits a queue missing from
    // the array.
    [Fact]
    public void BoardJs_UpdateCounts_ResetsDrainedQueuesAbsentFromTheSnapshot()
    {
        var js = BoardJs();

        // The set of queues the board tracks, zeroed on every snapshot before the present depths are
        // applied, so a drained queue clears rather than sticking at its last non-zero value.
        Assert.Contains("KNOWN_QUEUE_KEYS", js);
        Assert.Contains("baseDepth[key] = 0", js);
    }

    [Fact]
    public void Board_RecentSubmissionHeaders_ArePlainText_NotQueueLinks()
    {
        var board = Board();

        // The queue links live on the board boxes; the recent-submissions column headers don't
        // duplicate them — they are plain text.
        Assert.DoesNotContain(">Rules queue</a>", board);
        Assert.DoesNotContain(">Zendesk queue</a>", board);
        Assert.Contains(">Rules queue</th>", board);
        Assert.Contains(">Zendesk queue</th>", board);
    }

    [Fact]
    public void Board_StageAndDestinationBoxesCarryMeaningfulIcons()
    {
        var board = Board();

        // The stage and destination boxes render meaningful line-icons (not plain squares).
        Assert.Contains("obs-board__icon", board);
        Assert.Contains("StageIconSvg", board);
        Assert.Contains("DestinationIconSvg", board);
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

    // --- Per-hop travel: each glide lasts a fraction of THAT hop's dwell ---
    // Regression: the transform transition was a fixed 600ms while the dwell between hops scales with
    // the speed clock (dwellFor ≈ base·moveSpeed·jitter). Too long, a sped-up clock cut the glide off
    // and recoloured an envelope between the rules queue and the rules engine; a single short fixed
    // value instead made every hop teleport. The travel is now TRAVEL_FRACTION of the SAME dwell value
    // used for that hop's timer, so the glide always finishes before the next hop (no mid-lane
    // recolour) yet still reads as motion (no teleport) — slow into the long-dwell engine, brisk into
    // Submit.
    [Fact]
    public void BoardJs_TravelIsAFractionOfEachHopsOwnDwell()
    {
        var js = BoardJs();

        // setGlide derives the transform duration from a dwell value times the fraction.
        Assert.Contains("TRAVEL_FRACTION", js);
        Assert.Contains("dwellMs * TRAVEL_FRACTION", js);
        // A lane hop computes its dwell once and uses it for BOTH the glide and the hop timer.
        Assert.Contains("setGlide(token, d)", js);
        Assert.Contains("pausableTimeout(next, d)", js);
    }

    // --- An envelope flies INTO Submit rather than snapping there ---
    // The token is placed just off Submit's left with no transition, the start frame is committed
    // (a forced reflow), then it glides to the box — otherwise setting the transform in the same tick
    // the token is created snaps with no animation, so the envelope just "appears" at Submit.
    [Fact]
    public void BoardJs_EntryGlidesIntoSubmit_NotSnaps()
    {
        var js = BoardJs();

        Assert.Contains("void token.offsetWidth", js);              // commit the start frame
        Assert.Contains("submitAnchor.x - 90", js);                 // start off the left of Submit
    }

    // --- The matrix splits queue wait from processing using the recorded dequeue (started) time ---
    // Each stage now records when the consumer began processing it, so the grid shows the real queue
    // wait (submit/done → dequeue) and processing (dequeue → done) separately, instead of taking the
    // whole enqueue→done span as both the "queue" and the "engine" figure (which made the Rules engine
    // time equal the Zendesk-queue time and double-counted the duration).
    [Fact]
    public void BoardJs_SplitsQueueWaitFromProcessing_UsingStartedTimes()
    {
        var js = BoardJs();

        // The per-stage dequeue time is read off the event and folded into the row...
        Assert.Contains("startedOf", js);
        Assert.Contains("engineStart", js);
        Assert.Contains("ticketStart", js);
        // ...and the rules-engine cell is reached at the dequeue, with processing = dequeue → done.
        Assert.Contains("engineReached = r.engineStart || r.engine", js);
        Assert.Contains("dur(r.engineStart, r.engine)", js);
    }

    // --- A trickle burst must show EVERY envelope traversing every box, even when crowded ---
    // Regression: restAt used to hide any envelope beyond STACK_MAX_VISIBLE (visibility:hidden), so a
    // busy slow trickle overflowed the shared lane boxes (Submit / Rules-queue / Rules-engine) and the
    // hidden envelopes only reappeared at the less-crowded decision boxes — reading as if a message
    // teleported from Submit straight to a decision, skipping the queue and engine. An in-flight
    // envelope must stay visible; the overflow piles at the clamped diagonal offset and the "+N"
    // overlay conveys the true count instead.
    [Fact]
    public void BoardJs_KeepsTravellingEnvelopesVisible_DoesNotHideOverflow()
    {
        var js = BoardJs();

        // No envelope is hidden by the stacking overflow — they stay visible as they travel.
        Assert.DoesNotContain("visibility = 'hidden'", js);
        // The "+N" overlay still represents a crowded box.
        Assert.Contains("obs-board__stack-count", js);
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

        // The envelope fill reflects the outcome: approved green, rejected red, scrutiny yellow.
        Assert.Contains("DECISION_COLOURS", js);
        Assert.Contains("#00703c", js); // Approved green (canonical)
        Assert.Contains("#ffdd00", js); // Scrutiny yellow
        Assert.Contains("#d4351c", js); // Rejected red
    }

    [Fact]
    public void BoardJs_EnvelopesEnterBlueAndOnlyTakeColourAtAStatusBox()
    {
        var js = BoardJs();

        // Every envelope is created GDS blue and only repainted once it lands in a status box (or the
        // dead-letter queue, which is orange) — the outcome isn't known in-world until the engine runs.
        Assert.Contains("IN_FLIGHT_COLOUR", js);
        Assert.Contains("#1d70b8", js);          // in-flight blue
        Assert.Contains("DEADLETTER_COLOUR", js);
        Assert.Contains("#f47738", js);          // dead-letter orange
        Assert.Contains("paintToken", js);
        // makeToken creates the rect with the in-flight colour, not a pre-computed decision fill.
        Assert.Contains("fill=\"' + IN_FLIGHT_COLOUR +", js);
    }

    [Fact]
    public void BoardJs_EnvelopesStackAtEveryStage_AndDesyncPerMessage()
    {
        var js = BoardJs();

        // B7: every lane stage stacks (restAt with the per-stage anchor), feeding the "+N" overlay —
        // not just the terminal boxes, so a burst piling into Submit/the queues shows the overlay.
        Assert.Contains("restAt(token, 'stage:' + path[hop]", js);
        Assert.Contains("obs-board__stack-count", js);
        // B3: per-message dwell jitter + staggered burst spawns, so a batch trickles in and the
        // envelopes desync rather than marching out in one lump.
        Assert.Contains("DWELL_JITTER_FLOOR + Math.random()", js);
        Assert.Contains("setTimeout(spawnOne", js);
    }

    [Fact]
    public void BoardJs_DemoTrickleIsDrivenBySpeedAndCountSliders()
    {
        var js = BoardJs();

        // The slow-motion + single-step checkboxes were replaced by two demo-trickle sliders: Speed
        // (slow single-step crawl ↔ racing, via the dwell clock) and Messages (per burst).
        Assert.Contains("setDemoSpeed", js);
        Assert.Contains("setDemoCount", js);
        Assert.Contains("demoIntervalMs", js);
        Assert.DoesNotContain("setStepMode", js);
        Assert.DoesNotContain("setSlowMo", js);
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

        // A scrollable table the engine fills, newest submission at the top.
        Assert.Contains("data-obs-grid", board);
        Assert.Contains("Recent submissions", board);
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
