namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// Source-file assertion pattern: reads the Razor views from disk and asserts static
// Razor-source facts. The admin landing page (Views/Admin/Index.cshtml) renders each
// entry through the shared _AdminLandingTile partial, so the per-tile contracts are
// asserted against that partial. Mirrors SearchViewRenderTests and HelpControllerTests.
public sealed class AdminLandingViewTests
{
	private static string ReadWebView(string subFolder, string fileName)
	{
		var viewsDir = Path.GetFullPath(Path.Combine(
			AppContext.BaseDirectory,
			"..", "..", "..", "..", "..",
			"src", "DfE.CheckPerformanceData.Web", "Views", subFolder));
		return File.ReadAllText(Path.Combine(viewsDir, fileName));
	}

	private static string ReadIndexView() => ReadWebView("Admin", "Index.cshtml");
	private static string ReadTilePartial() => ReadWebView("Shared", "_AdminLandingTile.cshtml");

	// --- Tile_Renders_Live_Link_For_Enabled_Entry ---

	[Fact]
	public void Tile_Renders_Live_Link_For_Enabled_Entry()
	{
		var view = ReadTilePartial();

		// Contract: enabled entries render as a live GDS link inside an `if (entry.Enabled)` branch.
		Assert.Contains("entry.Enabled", view);
		Assert.Contains("govuk-link", view);
	}

	// --- Tile_Renders_Coming_Soon_Tile_For_Disabled_Entry ---

	[Fact]
	public void Tile_Renders_Coming_Soon_Tile_For_Disabled_Entry()
	{
		var view = ReadTilePartial();

		// Contract: disabled entries render with a "Coming soon" label and a muted GDS class.
		Assert.Contains("Coming soon", view);
		var hasMutedClass =
			view.Contains("govuk-tag--grey", StringComparison.Ordinal)
			|| view.Contains("govuk-hint", StringComparison.Ordinal);
		Assert.True(hasMutedClass, "Expected govuk-tag--grey or govuk-hint muted style.");
	}

	// --- AdminLanding_Links_Top_Level_Entries_That_Are_Navigable ---

	[Fact]
	public void AdminLanding_Links_Top_Level_Entries_That_Are_Navigable()
	{
		var view = ReadIndexView();

		// Every top-level entry used to be a pure container (empty Url) whose only purpose was
		// to head a list of child tiles. Dashboard is the first that is itself a page, so the
		// landing must link the section heading — otherwise the section is a dead end and the
		// page is reachable only from the sidebar.
		Assert.Contains("group.Entry.Url", view);
	}

	// --- AdminLanding_Does_Not_Raw_Render_Registry_Strings ---

	[Fact]
	public void AdminLanding_Does_Not_Raw_Render_Registry_Strings()
	{
		// XSS guard: registry-supplied strings (Title, Description, Url) must not flow through
		// Html.Raw — Razor's default encoding is the right path. The tile partial does use
		// Html.Raw, but only for the code-controlled heading tag (hN), never for entry fields.
		foreach (var view in new[] { ReadIndexView(), ReadTilePartial() })
		{
			Assert.False(view.Contains("Html.Raw(entry", StringComparison.Ordinal),
				"Registry-supplied entry fields must not be rendered via Html.Raw.");
			Assert.False(view.Contains("Html.Raw(node", StringComparison.Ordinal),
				"Registry-supplied node fields must not be rendered via Html.Raw.");
		}
	}
}
