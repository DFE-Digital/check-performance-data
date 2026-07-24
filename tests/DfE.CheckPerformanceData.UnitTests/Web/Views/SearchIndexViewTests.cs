using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Source-file assertion pattern: reads Views/Search/Index.cshtml from disk and asserts
// static Razor-source facts about the ISearchDebugOptions gate on the <!-- rank: -->
// HTML comment emission inside the single merged canonical-hit loop. Mirrors the
// read-helper + Assert.Contains style of AdminLandingViewTests. No WebApplicationFactory,
// no RazorProjectEngine — just the .cshtml text on disk.
public sealed class SearchIndexViewTests
{
	private static string ReadWebView(string subFolder, string fileName)
	{
		var viewsDir = Path.GetFullPath(Path.Combine(
			AppContext.BaseDirectory,
			"..", "..", "..", "..", "..",
			"src", "DfE.CheckPerformanceData.Web", "Views", subFolder));
		return File.ReadAllText(Path.Combine(viewsDir, fileName));
	}

	private static string ReadSearchIndexView() => ReadWebView("Search", "Index.cshtml");

	// --- SearchIndex_InjectsSearchDebugOptions_AtViewLevel ---

	[Fact]
	public void SearchIndex_InjectsSearchDebugOptions_AtViewLevel()
	{
		var view = ReadSearchIndexView();

		// Contract: the view @inject-s the narrow ISearchDebugOptions accessor at page
		// scope so the gate can read ShowSearchDebug directly in-view. Fully-qualified
		// namespace is asserted so a shortened variant via @using cannot mask a wrong
		// interface being injected.
		Assert.Contains(
			"@inject DfE.CheckPerformanceData.Application.Search.ISearchDebugOptions SearchDebugOptions",
			view);
	}

	// --- SearchIndex_RankCommentEmissions_AreGatedOnShowSearchDebug ---

	[Fact]
	public void SearchIndex_RankCommentEmissions_AreGatedOnShowSearchDebug()
	{
		var view = ReadSearchIndexView();

		// Contract 1: both emission lines still exist to be gated — non-goal is to
		// preserve the <!-- rank: N --> comment shape and F6 precision.
		Assert.Contains("<!-- rank:", view);

		// Contract 2: the accessor's ShowSearchDebug member is what the gate reads.
		Assert.Contains("SearchDebugOptions.ShowSearchDebug", view);

		// Contract 3: the emission inside the merged canonical-hit loop is wrapped in an
		// @if (SearchDebugOptions.ShowSearchDebug) block. One match is sufficient after
		// the split-list rewrite — the two per-corpus loops became one loop and the
		// gate lives inside it.
		var gateMatches = Regex.Matches(view, @"@if \(SearchDebugOptions\.ShowSearchDebug\)").Count;
		Assert.True(gateMatches >= 1,
			$"Expected @if (SearchDebugOptions.ShowSearchDebug) to wrap the <!-- rank: --> emission (>= 1 match); found {gateMatches}.");
	}

	// --- SearchIndex_RazorDocumentationComments_ArePreserved ---

	[Fact]
	public void SearchIndex_RazorDocumentationComments_ArePreserved()
	{
		var view = ReadSearchIndexView();

		// Contract: the Razor @* ... *@ documentation comment above the single merged
		// canonical-hit loop documents the aggregation rule (AggregateRank = MAX of
		// contributor raw ts_ranks) and points readers at SearchWeights.cs for the
		// per-field weight semantics. It never renders to HTML — it exists for future
		// readers — and the gate / loop edit must not delete it.
		Assert.Contains("AggregateRank", view);
		Assert.Contains("MAX", view);
		Assert.Contains("Application/Search/SearchWeights.cs", view);
	}
}
