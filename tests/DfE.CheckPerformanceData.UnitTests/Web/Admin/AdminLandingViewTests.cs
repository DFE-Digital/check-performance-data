namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// Source-file assertion pattern: reads the Razor view from disk and asserts static
// Razor-source facts. Mirrors SearchViewRenderTests and HelpControllerTests.
public sealed class AdminLandingViewTests
{
	private static string ReadIndexView()
	{
		var viewsDir = Path.GetFullPath(Path.Combine(
			AppContext.BaseDirectory,
			"..", "..", "..", "..", "..",
			"src", "DfE.CheckPerformanceData.Web", "Views", "Admin"));
		return File.ReadAllText(Path.Combine(viewsDir, "Index.cshtml"));
	}

	// --- IndexView_Renders_Live_Link_For_Enabled_Entry ---

	[Fact]
	public void IndexView_Renders_Live_Link_For_Enabled_Entry()
	{
		var view = ReadIndexView();

		// Contract: enabled entries render as a live GDS link inside an `if (entry.Enabled)` branch.
		Assert.Contains("entry.Enabled", view);
		Assert.Contains("govuk-link", view);
	}

	// --- IndexView_Renders_Coming_Soon_Tile_For_Disabled_Entry ---

	[Fact]
	public void IndexView_Renders_Coming_Soon_Tile_For_Disabled_Entry()
	{
		var view = ReadIndexView();

		// Contract: disabled entries render with a "Coming soon" label and a muted GDS class.
		Assert.Contains("Coming soon", view);
		var hasMutedClass =
			view.Contains("govuk-tag--grey", StringComparison.Ordinal)
			|| view.Contains("govuk-hint", StringComparison.Ordinal);
		Assert.True(hasMutedClass, "Expected govuk-tag--grey or govuk-hint muted style.");
	}

	// --- IndexView_Does_Not_Use_Html_Raw ---

	[Fact]
	public void IndexView_Does_Not_Use_Html_Raw()
	{
		var view = ReadIndexView();

		// XSS guard: registry-supplied strings (Title, Description, Url) must not flow
		// through Html.Raw — Razor's default encoding is the right path.
		Assert.DoesNotContain("@Html.Raw", view);
	}
}
