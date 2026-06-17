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
	public void Index_RendersACollapsibleDemoPanel_GatedOnDemoToolsEnabled()
	{
		var index = ReadView("Index.cshtml");
		var panel = ReadView("_DemoPanel.cshtml");

		// The dashboard pulls in the Demo panel partial; the panel itself is gated on the flag and
		// loads the AJAX-drive script only when rendered.
		Assert.Contains("_DemoPanel", index);
		Assert.Contains("Model.DemoToolsEnabled", index); // guards the uat-console.js include
		Assert.Contains("uat-console.js", index);

		// The panel only renders when the controller resolved the dev/test gate, behind a
		// collapsible GDS <details> opened by a "Demo" toggle (hidden by default).
		Assert.Contains("Model.DemoToolsEnabled", panel);
		Assert.Contains("govuk-details", panel);
		Assert.Contains(">Demo</span>", panel);
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

	// --- One shared decision-colour legend below the charts, referenced once ---

	[Fact]
	public void DecisionLegend_IsOneSharedPartial_ReadingTheCanonicalColours()
	{
		var legend = ReadView("_DecisionLegend.cshtml");
		Assert.Contains("DecisionColours.LegendItems", legend);
		Assert.Contains("obs-legend", legend);

		var index = ReadView("Index.cshtml");
		// Referenced exactly once, beneath the chart group.
		var first = index.IndexOf("_DecisionLegend", System.StringComparison.Ordinal);
		Assert.True(first >= 0, "Index must render the shared decision legend.");
		Assert.Equal(first, index.LastIndexOf("_DecisionLegend", System.StringComparison.Ordinal));
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
