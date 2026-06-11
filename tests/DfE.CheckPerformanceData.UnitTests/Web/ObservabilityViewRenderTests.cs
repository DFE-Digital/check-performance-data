namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Static Razor-source assertions for the observability dashboard views, mirroring
// LayoutRenderTests: read the .cshtml as text and assert on the source. Hostless — no MVC
// test harness — while still pinning the markup contracts most likely to regress: the
// range/granularity form, the chart headings tracking the selected window, and each chart
// shipping its paired accessible data table.
public sealed class ObservabilityViewRenderTests
{
	private static string ReadView(string name)
	{
		var thisFile = ThisFilePath();
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
		var view = Path.Combine(repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Observability", name);
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
}
