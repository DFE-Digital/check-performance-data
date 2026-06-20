namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Static Razor-source assertions pinning the single shared pager contract: every paged list renders
// through Views/Shared/_Pager.cshtml (so the windowing lives in exactly one place), and no view
// keeps its own "list every page 1..N" loop — the bug behind the 82-pages-down-the-bottom report.
public sealed class PagerViewRenderTests
{
	private static string ReadView(string relativePath)
	{
		var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
		return File.ReadAllText(Path.Combine(
			repoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", Path.Combine(relativePath.Split('/'))));
	}

	private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

	[Fact]
	public void SharedPager_WindowsThroughPageNumbers_AndRendersAnEllipsis()
	{
		var pager = ReadView("Shared/_Pager.cshtml");

		// The windowing comes from the tested pure helper, not an inline loop in the partial.
		Assert.Contains("PageNumbers.Build", pager);
		// GDS markup: numbered list, ellipsis item, and the prev/next arrow icons.
		Assert.Contains("govuk-pagination__list", pager);
		Assert.Contains("govuk-pagination__item--ellipses", pager);
		Assert.Contains("govuk-pagination__icon--prev", pager);
		Assert.Contains("govuk-pagination__icon--next", pager);
	}

	[Theory]
	[InlineData("Observability/Transactions.cshtml")]
	[InlineData("QueueAdmin/QueueList.cshtml")]
	[InlineData("Help/Deleted.cshtml")]
	[InlineData("Help/Search.cshtml")]
	[InlineData("CheckYourPupilData/_Pagination.cshtml")]
	public void EveryPagedView_RendersThroughTheSharedPager(string view)
	{
		Assert.Contains("\"_Pager\"", ReadView(view));
	}

	[Theory]
	[InlineData("Observability/Transactions.cshtml")]
	[InlineData("QueueAdmin/QueueList.cshtml")]
	[InlineData("Help/Deleted.cshtml")]
	[InlineData("Help/Search.cshtml")]
	[InlineData("CheckYourPupilData/_Pagination.cshtml")]
	public void NoPagedView_KeepsItsOwnFullPageLoop(string view)
	{
		var source = ReadView(view);

		// None of the list pages may render their own pagination markup any more — the shared control
		// owns it, so a long list can never list every page again.
		Assert.DoesNotContain("govuk-pagination__list", source);
		Assert.DoesNotContain("<govuk-pagination>", source);
		Assert.DoesNotContain("<= Model.TotalPages", source);
		Assert.DoesNotContain("< Model.TotalPages; i++", source);
	}
}
