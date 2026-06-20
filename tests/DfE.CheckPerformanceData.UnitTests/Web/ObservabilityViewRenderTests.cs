namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Static Razor-source assertions for the observability dashboard views, mirroring
// LayoutRenderTests: read the .cshtml as text and assert on the source. Hostless — no MVC
// test harness — while still pinning the markup contracts most likely to regress: the
// range/granularity form, the chart headings tracking the selected window, and each chart
// shipping its paired accessible data table.
public sealed class ObservabilityViewRenderTests
{
	private static string ReadView(string name, string folder = "Observability")
	{
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var view = Path.Combine(repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", folder, name);
		return File.ReadAllText(view);
	}

	private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
		=> path;

	// --- The dashboard carries a GET form selecting the chart window and bucket size ---

	[Fact]
	public void Index_HasRangeAndGranularityForm_SubmittingByGet()
	{
		var view = ReadView("Index.cshtml");
		Assert.Contains("method=\"get\"", view);
		Assert.Contains("name=\"range\"", view);
		Assert.Contains("name=\"granularity\"", view);
		// GDS-styled controls, not bare HTML.
		Assert.Contains("govuk-select", view);
		Assert.Contains("govuk-label", view);
	}

	// --- The chart headings and aria summaries follow the selected window, not a fixed 24h ---

	[Fact]
	public void Chart_HeadingsTrackTheSelectedWindow()
	{
		var view = ReadView("_Chart.cshtml");
		Assert.Contains("Model.RangeLabel", view);
		Assert.DoesNotContain("(last 24 hours)", view);
		Assert.DoesNotContain("over the last 24 hours", view);
	}

	// --- The per-stage dwell chart renders the queried Dwell series with its paired table ---

	[Fact]
	public void DwellChart_RendersBarsWithAPairedDataTable()
	{
		var view = ReadView("_DwellChart.cshtml");
		// Accessible-chart contract: role="img" + aria-label SVG, decorative marks hidden,
		// and a govuk-table carrying the identical numbers as the source of truth.
		Assert.Contains("Model.Dwell", view);
		Assert.Contains("role=\"img\"", view);
		Assert.Contains("aria-label", view);
		Assert.Contains("govuk-table", view);
		Assert.Contains("Average wait", view);
		// Empty state copy, never a broken axis box.
		Assert.Contains("No data for this range", view);
	}

	[Fact]
	public void Index_IncludesTheDwellChart()
	{
		var view = ReadView("Index.cshtml");
		Assert.Contains("_DwellChart", view);
	}

	// --- The decision-mix-over-time chart: one line per status, paired table, deploy markers ---

	[Fact]
	public void DecisionMixOverTimeChart_RendersSeriesWithAPairedDataTable()
	{
		var view = ReadView("_DecisionMixOverTimeChart.cshtml");
		Assert.Contains("Model.DecisionMixOverTime", view);
		Assert.Contains("role=\"img\"", view);
		Assert.Contains("aria-label", view);
		Assert.Contains("govuk-table", view);
		// Series legend in text — never colour alone.
		Assert.Contains("obs-legend", view);
		// Empty state copy, never a broken axis box.
		Assert.Contains("No data for this range", view);
	}

	[Fact]
	public void Index_IncludesTheDecisionMixOverTimeChart()
	{
		var view = ReadView("Index.cshtml");
		Assert.Contains("_DecisionMixOverTimeChart", view);
	}

	// --- Admin surfaces showing a reference link it to the journey timeline ---

	[Theory]
	[InlineData("WorkingMessage.cshtml")]
	[InlineData("Message.cshtml")]
	[InlineData("Dlq.cshtml")]
	public void QueueAdminViews_LinkAReferenceToItsJourney(string viewName)
	{
		var view = ReadView(viewName, "QueueAdmin");
		Assert.Contains("/admin/observability/journey/", view);
		Assert.Contains("Track this request", view);
	}

	[Fact]
	public void InspectPanel_LinksToTheFullJourneyTimeline()
	{
		var view = ReadView("Inspect.cshtml");
		Assert.Contains("/admin/observability/journey/", view);
	}

	// --- Deploy markers render through ONE shared partial on both time-axis charts ---

	[Fact]
	public void DeployMarkers_AreASharedPartial_UsedByBothTimeCharts()
	{
		var markers = ReadView("_DeployMarkers.cshtml");
		Assert.Contains("stroke-dasharray", markers);
		Assert.Contains("marker.Label", markers);

		var throughput = ReadView("_Chart.cshtml");
		Assert.Contains("_DeployMarkers", throughput);
		// The inline marker loop is gone from the throughput chart — no duplicated rendering.
		Assert.DoesNotContain("stroke-dasharray", throughput);

		var overTime = ReadView("_DecisionMixOverTimeChart.cshtml");
		Assert.Contains("_DeployMarkers", overTime);
		Assert.DoesNotContain("stroke-dasharray", overTime);
	}

	// --- The Demo panel folds the old Debug Pipeline controls into the dashboard, dev-gated ---

	[Fact]
	public void Index_RendersADemoPanel_GatedOnDemoToolsEnabled_WithADecoupledToggleBelowTheTable()
	{
		var index = ReadView("Index.cshtml");
		var panel = ReadView("_DemoPanel.cshtml");
		var board = ReadView("_Board.cshtml");

		// The dashboard pulls in the Demo panel partial; the panel itself is gated on the flag and
		// loads the AJAX-drive script only when rendered.
		Assert.Contains("_DemoPanel", index);
		Assert.Contains("Model.DemoToolsEnabled", index); // guards the uat-console.js include
		Assert.Contains("uat-console.js", index);

		// The panel only renders when the controller resolved the dev/test gate, as an addressable
		// region (no longer a GDS <details> — its toggle is decoupled below the table).
		Assert.Contains("Model.DemoToolsEnabled", panel);
		Assert.Contains("data-obs-demo-panel", panel);
		Assert.Contains("id=\"obs-demo-panel\"", panel);

		// The trigger is a subtle toggle rendered below the Recent submissions table, controlling the
		// panel above by aria-controls; it is gated on the same flag as the panel.
		Assert.Contains("data-obs-demo-toggle", board);
		Assert.Contains("aria-controls=\"obs-demo-panel\"", board);
		Assert.Contains("Model.DemoToolsEnabled", board);
	}

	[Fact]
	public void Index_DemoPanel_CarriesAllDriveInjectAndSeedControls()
	{
		var panel = ReadView("_DemoPanel.cshtml");

		// The drive presets, inject-failing and seed-DLQ all post to the retained /dev/uat POST
		// endpoints (the GET page is gone; the endpoints survive for the AJAX drives).
		Assert.Contains("/dev/uat/drive?outcome=approved", panel);
		Assert.Contains("/dev/uat/drive?outcome=rejected", panel);
		Assert.Contains("/dev/uat/drive?outcome=scrutiny", panel);
		Assert.Contains("/dev/uat/inject-failure", panel);
		Assert.Contains("/dev/uat/seed-dlq", panel);
		// Seed a couple of months of synthetic history so the charts look full on a fresh dev env.
		Assert.Contains("/dev/uat/seed-messages", panel);
		Assert.Contains("Seed messages", panel);
		// Purge all demo traffic (keeping real submissions) behind a confirm.
		Assert.Contains("/dev/uat/purge-demo", panel);
		Assert.Contains("Purge demo traffic", panel);
		Assert.Contains("data-uat-confirm", panel);
		// The batch-size input the drives mirror onto each form.
		Assert.Contains("data-uat-batch", panel);
		// AJAX-drive hooks so a drive refreshes the board in place rather than reloading.
		Assert.Contains("uat-inline-form", panel);
	}

	[Fact]
	public void Index_DemoPanel_HoldsTheReplayAndBoardDemoControls()
	{
		var panel = ReadView("_DemoPanel.cshtml");

		// The replay scrubber and the demo checkboxes moved out of the standalone board boxes into
		// the one Demo panel: the panel now carries those control hooks directly.
		Assert.Contains("Replay recent traffic", panel);
		Assert.Contains("data-obs-scrubber", panel);
		Assert.Contains("data-obs-slowmo", panel);
		Assert.Contains("data-obs-step", panel);
		Assert.Contains("data-obs-demo", panel);
	}

	[Fact]
	public void Index_DemoPanel_ReplayHasASelectableWindowDefaultingToTheLastDay()
	{
		var panel = ReadView("_DemoPanel.cshtml");

		// A window select drives how far back the replay reaches; the 24-hour option is the default
		// so "Event N of M" reflects a recent window rather than every event ever recorded.
		Assert.Contains("data-obs-replay-window", panel);
		Assert.Contains("Last 24 hours", panel);
		Assert.Contains("value=\"1440\" selected", panel);
	}

	[Fact]
	public void Index_DemoPanel_LaysReplayAndDemoControlsOnOneHorizontalRow()
	{
		var panel = ReadView("_DemoPanel.cshtml");

		// The replay box and the board demo controls share one row so the panel stays short.
		Assert.Contains("obs-demo-panel__row", panel);
	}

	[Fact]
	public void Tiles_CarryLiveUpdateHooksForTheBoardEngine()
	{
		var view = ReadView("_Tiles.cshtml");
		// The board engine targets these hooks to keep the figures live as traffic is driven.
		Assert.Contains("data-obs-tile-processed", view);
		Assert.Contains("data-obs-tile-depth", view);
	}

	[Fact]
	public void Tiles_RenderTheTwentyFourHourDecisionCounters_WithLiveHooks()
	{
		var view = ReadView("_Tiles.cshtml");
		// Ongoing 24-hour counts beside "processed today": auto-approved / auto-rejected / scrutiny
		// plus the current dead-letter count, each server-rendered and carrying a live-update hook
		// the board engine ticks as a decided/failed submission completes.
		Assert.Contains("data-obs-tile-approved", view);
		Assert.Contains("data-obs-tile-rejected", view);
		Assert.Contains("data-obs-tile-scrutiny", view);
		Assert.Contains("data-obs-tile-deadletter", view);
		Assert.Contains("Model.AutoApprovedToday", view);
		Assert.Contains("Model.AutoRejectedToday", view);
		Assert.Contains("Model.ScrutinyToday", view);
		Assert.Contains("Model.DeadLetterCount", view);
	}

	[Fact]
	public void Board_ServerRendersTheRecentSubmissionsMatrix_WithPerStageTimeAndDuration_AndASeed()
	{
		var board = ReadView("_Board.cshtml");

		// The matrix is server-rendered from the grouped recent submissions, so it is populated on
		// load rather than empty until live traffic arrives.
		Assert.Contains("Model.RecentSubmissions", board);
		// Each stage cell stacks the arrival time over the time spent there.
		Assert.Contains("obs-cell-time", board);
		Assert.Contains("obs-cell-dur", board);
		// A JSON seed beside the table lets the board engine populate its live grid from the
		// server-rendered history without wiping it on the first re-render.
		Assert.Contains("data-obs-grid-seed", board);
	}

	[Fact]
	public void BoardEngine_SeedsItsLiveGridFromTheServerRenderedHistory()
	{
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var js = File.ReadAllText(Path.Combine(
			repoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", "observability-board.js"));

		// The engine reads the seed blob and primes its grid (and animatedRefs) before any feed
		// connects, and renders the stacked time/duration cells.
		Assert.Contains("data-obs-grid-seed", js);
		Assert.Contains("seedGridFromServer", js);
		Assert.Contains("obs-cell-time", js);
		Assert.Contains("obs-cell-dur", js);
	}

	[Fact]
	public void Board_EveryBoxCarriesALiveInFlightCountHook()
	{
		var board = ReadView("_Board.cshtml");

		// Every box — the five stage lanes plus the decision and dead-letter boxes — carries a
		// live-count badge the engine fills as envelopes flow through, so a cluster is seen entering
		// each box. Hidden while zero. The lane/decision keys are Razor-bound; the DLQ is literal.
		Assert.Contains("data-live-count=\"@stage.Key\"", board);   // the five stage lanes
		Assert.Contains("data-live-count=\"@decision.Key\"", board); // the three decision boxes
		Assert.Contains("data-live-count=\"dlq\"", board);          // the dead-letter box
		Assert.Contains("obs-board__live-count", board);
	}

	[Fact]
	public void BoardEngine_KeepsEveryBoxInFlightCountLive()
	{
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var js = File.ReadAllText(Path.Combine(
			repoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", "observability-board.js"));

		// The engine moves a token's count from the box it leaves to the box it enters, folds the
		// queue depth into the same badge, and clears the count when the envelope is removed.
		Assert.Contains("data-live-count", js);
		Assert.Contains("moveToken", js);
		Assert.Contains("clearToken", js);
		Assert.Contains("baseDepth", js);
	}

	[Fact]
	public void BoardEngine_ReplaysOnePerSubmission_WithAnRnCopyThatFillsTheMatrix()
	{
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var js = File.ReadAllText(Path.Combine(
			repoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", "observability-board.js"));

		// Replay is per-SUBMISSION, not per stage row (the bug where a Submitted row vanished at
		// Submit and a TicketCreated row crossed the top boxes with no status). Each replayed
		// submission flies one envelope along the full path and is added to the matrix as a
		// <root>-R{n} copy.
		Assert.Contains("replaySubmission", js);
		Assert.Contains("groupByReference", js);
		Assert.Contains("'-R'", js);
		// The scrubber and the picker both drive whole submissions through that one path.
		Assert.Contains("feed.onMessage", js);
	}

	[Fact]
	public void ThroughputChart_RendersAxisValueLabels()
	{
		var view = ReadView("_Chart.cshtml");

		// The throughput chart was unreadable — axis lines but no values. It now builds Y-axis tick
		// values (0 / mid / peak) and thinned X-axis time labels as SVG text, emitted via Html.Raw.
		Assert.Contains("yTicks", view);
		Assert.Contains("axisDecorations", view);
		Assert.Contains("Html.Raw(axisDecorations)", view);
		// X labels are thinned so adjacent times never overlap.
		Assert.Contains("maxXTicks", view);
	}

	[Fact]
	public void Charts_AreEnlargeableInAModalOnClick()
	{
		var index = ReadView("Index.cshtml");
		Assert.Contains("observability-chart-modal.js", index);

		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var js = File.ReadAllText(Path.Combine(
			repoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", "observability-chart-modal.js"));

		// Clicking a chart (or its Enlarge button) opens a scaled clone in a native dialog so the
		// small labels are readable.
		Assert.Contains("obs-chart-panel", js);
		Assert.Contains("Enlarge chart", js);
		Assert.Contains("showModal", js);
	}

	[Fact]
	public void UatConsole_StaggersABatchedDriveIntoAVisibleCluster()
	{
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var js = File.ReadAllText(Path.Combine(
			repoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", "uat-console.js"));

		// A batch of more than one drive fires one at a time with randomised gaps, so a cluster
		// appears and flows through the board rather than a single envelope for the whole batch.
		Assert.Contains("staggerDrives", js);
		Assert.Contains("submitDriveForm(form, 1)", js);
		Assert.Contains("Math.random", js);
	}

	[Fact]
	public void Transactions_HasAReferenceSearchBox()
	{
		var view = ReadView("Transactions.cshtml");

		// A GDS search input named 'reference', submitted by the existing GET form, drives the filter.
		Assert.Contains("name=\"reference\"", view);
		Assert.Contains("Search by reference", view);
		// The active term is carried back so the box stays populated and the pager preserves it.
		Assert.Contains("Model.Reference", view);
	}

	[Fact]
	public void Transactions_UsesTheWideTemplate_AndOffersGroupByMessage()
	{
		var view = ReadView("Transactions.cshtml");

		// Wide template so the grid is not squished; a "Group by message" checkbox switches to the
		// one-row-per-message matrix across the pipeline stages.
		Assert.Contains("_AdminWideLayout", view);
		Assert.Contains("Group by submission", view);
		Assert.Contains("name=\"group\"", view);
		Assert.Contains("obs-tx-grouped", view);
		Assert.Contains("Zendesk ticket", view);
	}

	[Fact]
	public void Transactions_UngroupedRowShowsItsOwnEventDecision_NotTheResolvedOne()
	{
		var view = ReadView("Transactions.cshtml");

		// Fix: a per-event row shows only the decision its own stage carries (RulesEvaluated), so a
		// Submitted row's decision cell is blank rather than borrowing the message's eventual
		// decision (which read as "the decision existed at submit time"). The resolved decision is
		// shown once per message in the grouped view instead.
		Assert.Contains("row.DecisionStatus", view);
		Assert.DoesNotContain("row.ResolvedDecision", view);
	}

	[Fact]
	public void DemoPanel_HostsTheReplaySubmissionPicker_InPlaceOfTheStandalonePages()
	{
		var panel = ReadView("_DemoPanel.cshtml");

		// The submission picker (search recent submissions, tick, Play through the board) now lives
		// in the Demo panel — the standalone submissions + walkthrough pages were retired.
		Assert.Contains("data-obs-replay-picker", panel);
		Assert.Contains("data-obs-picker-load", panel);
		Assert.Contains("data-obs-picker-play", panel);
	}

	[Fact]
	public void Board_NoLongerLinksToAStandaloneReplayPage()
	{
		var board = ReadView("_Board.cshtml");

		// The "Replay submissions through the stages" link to /submissions is gone; replay is in the
		// dashboard Demo panel now.
		Assert.DoesNotContain("/admin/observability/submissions", board);
	}

	[Fact]
	public void Board_NoLongerCarriesItsOwnReplayOrDemoControlBoxes()
	{
		// The board partial is now pure board: the controls live in the dashboard's Demo panel, so
		// the partial must not render its own replay group or dev-only demo-control checkboxes.
		var board = ReadView("_Board.cshtml");

		Assert.DoesNotContain("obs-board__controls", board);
		Assert.DoesNotContain("obs-board__replay", board);
		Assert.DoesNotContain("obs-board__dev-controls", board);
		Assert.DoesNotContain("data-obs-slowmo", board);
	}

	// --- The decision-mix totals chart is a pie with the value in each slice ---

	[Fact]
	public void DecisionMixChart_IsAPieWithValueLabelsAndAPairedTable()
	{
		var view = ReadView("_Chart.cshtml");
		// Bespoke SVG pie: slices are arc paths (plus a full-circle case), not bars.
		Assert.Contains("Decision mix", view);
		Assert.Contains("<path", view);
		Assert.Contains("obs-pie__label", view);
		// The slice label shows the count and the percentage.
		Assert.Contains("entry.Count (@pct%)", view);
		// The bar-chart rendering is gone.
		Assert.DoesNotContain("var barWidth", view);
		// Still accessible: role="img" + aria-label + the paired data table.
		Assert.Contains("role=\"img\"", view);
		Assert.Contains("Decision mix: @summary", view);
		Assert.Contains("govuk-table", view);
	}

	// --- Decision colours come from ONE canonical map, consumed by every decision chart ---

	[Fact]
	public void DecisionColours_AreTheSingleSourceConsumedByEveryDecisionChart()
	{
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var map = File.ReadAllText(Path.Combine(
			repoRoot, "src", "DfE.CheckPerformanceData.Web", "Models", "Observability", "DecisionColours.cs"));

		// The canonical GDS colours live in one place.
		Assert.Contains("#00703c", map); // AutoApproved (green)
		Assert.Contains("#d4351c", map); // AutoRejected (red)
		Assert.Contains("#f47738", map); // Scrutiny (orange)

		// Both decision charts read the map rather than hard-coding hex literals.
		var pie = ReadView("_Chart.cshtml");
		Assert.Contains("DecisionColours.For", pie);
		Assert.DoesNotContain("#00703c", pie);
		Assert.DoesNotContain("#f47738", pie);

		var overTime = ReadView("_DecisionMixOverTimeChart.cshtml");
		Assert.Contains("DecisionColours.For", overTime);
		Assert.DoesNotContain("#00703c", overTime);
		Assert.DoesNotContain("#f47738", overTime);
	}

	// --- Round 3: each colour-coded chart carries its OWN legend beneath it; the single shared
	//     lower-left legend is gone ---

	[Fact]
	public void DecisionMixPie_CarriesItsOwnLegendBeneathIt()
	{
		var pie = ReadView("_Chart.cshtml");
		// The pie now states its own colour key directly beneath it, reading the canonical map.
		Assert.Contains("DecisionColours.LegendItems", pie);
		Assert.Contains("obs-legend", pie);
	}

	[Fact]
	public void Index_NoLongerRendersTheSingleSharedDecisionLegend()
	{
		var index = ReadView("Index.cshtml");
		// The shared lower-left legend was removed in favour of per-chart legends.
		Assert.DoesNotContain("_DecisionLegend", index);
	}

	// --- The chart data-table disclosures toggle exactly once (no open/close flicker) ---

	[Fact]
	public void ExportScript_OwnsChartDisclosureToggleAsASingleSourceOfTruth()
	{
		// The chart "View … data table" disclosures flickered open/closed because more than one
		// toggle path fired per click. The dashboard script now owns the toggle as the single
		// source of truth: it intercepts the summary click (preventDefault), flips details.open
		// once behind a re-entrancy guard, and binds each summary only once.
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var js = File.ReadAllText(Path.Combine(
			repoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", "observability-export.js"));

		// Targets the chart panels' disclosures specifically.
		Assert.Contains("obs-chart-panel", js);
		Assert.Contains("details", js);
		Assert.Contains("summary", js);
		// Exactly one toggle: the native default is suppressed and open is flipped once.
		Assert.Contains("preventDefault", js);
		Assert.Contains(".open", js);
		// Bound once per summary (idempotency guard), so re-init cannot stack handlers.
		Assert.Contains("dataset", js);
	}

	// --- Print/PDF: logical blocks stay whole and the board prints on its own page ---

	[Fact]
	public void ObservabilityCss_KeepsBlocksWholeWhenPrintingToPdf()
	{
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var css = File.ReadAllText(Path.Combine(
			repoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "css", "observability.css"));

		// A print stylesheet exists and stops the board and chart panels being split across pages.
		Assert.Contains("@media print", css);
		Assert.Contains("break-inside: avoid", css);
		// The board gets its own page.
		Assert.Contains("break-before: page", css);
		Assert.Contains("break-after: page", css);
		// Interactive-only chrome is hidden in print.
		Assert.Contains(".obs-range-form", css);
		Assert.Contains(".obs-demo-panel", css);
	}

	// --- The hidden attribute must actually hide the reconnect notice ---

	[Fact]
	public void ObservabilityCss_RestoresTheHiddenAttributeOnTheReconnectNotice()
	{
		// dfefrontend.css declares `p { display: block }`, and any author rule outranks the
		// UA stylesheet's `[hidden] { display: none }` — so a hidden paragraph stays visible.
		// The board engine toggles the reconnect notice via the hidden attribute, so the
		// stylesheet must restore its meaning explicitly.
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var css = File.ReadAllText(Path.Combine(
			repoRoot, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "css", "observability.css"));

		Assert.Contains(".obs-board__reconnect[hidden]", css);
	}
}
