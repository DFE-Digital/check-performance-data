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

	// --- SearchIndex_CorpusBadge_IsGatedOnShowSearchDebug ---

	[Fact]
	public void SearchIndex_CorpusBadge_IsGatedOnShowSearchDebug()
	{
		var view = ReadSearchIndexView();

		// Contract: the corpus badge markup that leans on hit.BlockContributorCount
		// only renders when SearchDebugOptions.ShowSearchDebug is true. Real users
		// must never see it. Structurally: an @if (SearchDebugOptions.ShowSearchDebug)
		// gate wraps (directly or transitively) at least one reference to
		// hit.BlockContributorCount inside the same block. We assert this with a
		// dot-all regex that requires the gate to appear before a subsequent
		// hit.BlockContributorCount reference within a reasonable window (the whole
		// file is small — a lazy match across the file body suffices).
		var gateThenBlockContributor = new Regex(
			@"@if\s*\(SearchDebugOptions\.ShowSearchDebug\)[\s\S]*?hit\.BlockContributorCount",
			RegexOptions.Multiline);
		Assert.True(gateThenBlockContributor.IsMatch(view),
			"Expected an @if (SearchDebugOptions.ShowSearchDebug) gate to wrap a hit.BlockContributorCount reference (badge markup); no such gate-then-BlockContributorCount sequence found.");
	}

	// --- SearchIndex_PureBlockCorpusBadge_UsesContentBlockLabel ---

	[Fact]
	public void SearchIndex_PureBlockCorpusBadge_UsesContentBlockLabel()
	{
		var view = ReadSearchIndexView();

		// Contract: rows whose only contributor is a block (BlockContributorCount > 0
		// AND PageContributorCount == 0) render a GDS grey tag with the literal label
		// "Content block". Source-file heuristic: both substrings must be present.
		Assert.Contains("govuk-tag govuk-tag--grey", view);
		Assert.Contains("Content block", view);
		Assert.Contains("PageContributorCount == 0", view);
	}

	// --- SearchIndex_HybridCorpusBadge_UsesBlocksNCountLabel ---

	[Fact]
	public void SearchIndex_HybridCorpusBadge_UsesBlocksNCountLabel()
	{
		var view = ReadSearchIndexView();

		// Contract: rows with both a page contributor and one or more block
		// contributors render a GDS grey tag with the literal label
		// "Blocks: @hit.BlockContributorCount" (e.g. "Blocks: 3"). Source-file
		// heuristic: the exact Razor substring plus the govuk-tag class.
		Assert.Contains("govuk-tag govuk-tag--grey", view);
		Assert.Contains("Blocks: @hit.BlockContributorCount", view);
	}

	// --- SearchIndex_AggregateRankComment_UsesF6InvariantCulture ---

	[Fact]
	public void SearchIndex_AggregateRankComment_UsesF6InvariantCulture()
	{
		var view = ReadSearchIndexView();

		// Contract: the <!-- rank: --> HTML comment reads the canonical row's
		// AggregateRank (not hit.Rank — that accessor doesn't exist on
		// CanonicalSearchHit) and formats it with the "F6" specifier under
		// System.Globalization.CultureInfo.InvariantCulture so the debug output stays
		// stable across culture / locale.
		Assert.Contains("hit.AggregateRank", view);
		Assert.Contains("\"F6\"", view);
		Assert.Contains("System.Globalization.CultureInfo.InvariantCulture", view);
		Assert.DoesNotContain("hit.Rank.ToString", view);
	}
}
