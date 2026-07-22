using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Source-file assertion pattern: reads Views/Search/Index.cshtml from disk and asserts
// static Razor-source facts about the ISearchDebugOptions gate on the two <!-- rank: -->
// HTML comment emissions (page-hit loop + content-block-hit loop). Mirrors the
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

		// Contract 3: both emissions (page-hit + content-block-hit) are wrapped in a
		// distinct @if (SearchDebugOptions.ShowSearchDebug) block. Two matches proves
		// the gate is applied at each of the two hit loops, not just one.
		var gateMatches = Regex.Matches(view, @"@if \(SearchDebugOptions\.ShowSearchDebug\)").Count;
		Assert.True(gateMatches >= 2,
			$"Expected @if (SearchDebugOptions.ShowSearchDebug) to wrap both <!-- rank: --> emissions (>= 2 matches); found {gateMatches}.");
	}

	// --- SearchIndex_RazorDocumentationComments_ArePreserved ---

	[Fact]
	public void SearchIndex_RazorDocumentationComments_ArePreserved()
	{
		var view = ReadSearchIndexView();

		// Contract: the Razor @* ... *@ documentation comments above each emission
		// document the ranking weight semantics (Keywords A, Title B, Subtitle C,
		// BodyPlainText D on page hits; Keywords A + ValuePlainText B on content
		// blocks). They never render to HTML — they exist for future readers — and
		// the gate edit must not delete them.
		Assert.Contains(
			"Debug: ts_rank score (Keywords A + Title B + Subtitle C on PageNode.SearchVector",
			view);
		Assert.Contains(
			"Debug: ts_rank score on ContentBlock.SearchVector (Keywords A + ValuePlainText B).",
			view);
	}
}
