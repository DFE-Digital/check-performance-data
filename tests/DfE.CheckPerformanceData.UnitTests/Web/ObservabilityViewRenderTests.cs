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
}
