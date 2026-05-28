namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// Source-file assertion pattern: reads the Razor partial / layout / stylesheet
// from disk and asserts static facts about their content. Mirrors
// AdminLandingViewTests for the same disk-path resolution and Assert.Contains style.
public sealed class AdminNavTreeRenderTests
{
	private static string SharedViewsDir => Path.GetFullPath(Path.Combine(
		AppContext.BaseDirectory,
		"..", "..", "..", "..", "..",
		"src", "DfE.CheckPerformanceData.Web", "Views", "Shared"));

	private static string CssPath => Path.GetFullPath(Path.Combine(
		AppContext.BaseDirectory,
		"..", "..", "..", "..", "..",
		"src", "DfE.CheckPerformanceData.Web", "wwwroot", "css", "site.css"));

	private static string ReadAdminNavTree() =>
		File.ReadAllText(Path.Combine(SharedViewsDir, "_AdminNavTree.cshtml"));

	private static string ReadAdminLayout() =>
		File.ReadAllText(Path.Combine(SharedViewsDir, "_AdminLayout.cshtml"));

	private static string ReadSiteCss() =>
		File.ReadAllText(CssPath);

	// --- AdminNavTree_File_Exists ---

	[Fact]
	public void AdminNavTree_File_Exists()
	{
		var viewPath = Path.Combine(SharedViewsDir, "_AdminNavTree.cshtml");
		Assert.True(File.Exists(viewPath), $"Expected partial at {viewPath}");
	}

	// --- AdminNavTree_Declares_AdminLandingViewModel_Model ---

	[Fact]
	public void AdminNavTree_Declares_AdminLandingViewModel_Model()
	{
		var src = ReadAdminNavTree();

		Assert.Contains("@model", src);
		Assert.Contains("AdminLandingViewModel", src);
	}

	// --- AdminNavTree_Renders_Nav_Wrapper_With_AriaLabel ---

	[Fact]
	public void AdminNavTree_Renders_Nav_Wrapper_With_AriaLabel()
	{
		var src = ReadAdminNavTree();

		Assert.Contains("aria-label=\"Administration navigation\"", src);
	}

	// --- AdminNavTree_Renders_tv_root_Root_List ---

	[Fact]
	public void AdminNavTree_Renders_tv_root_Root_List()
	{
		var src = ReadAdminNavTree();

		// Reuses the existing .tv-* CSS / site.js auto-bind selector; do NOT fork.
		Assert.Contains("tv tv-root", src);
	}

	// --- AdminNavTree_Renders_Administration_Home_Link ---

	[Fact]
	public void AdminNavTree_Renders_Administration_Home_Link()
	{
		var src = ReadAdminNavTree();

		Assert.Contains("/admin", src);
		Assert.Contains("Administration", src);
	}

	// --- AdminNavTree_Disabled_Child_Renders_AriaDisabled_Span_Not_Anchor ---

	[Fact]
	public void AdminNavTree_Disabled_Child_Renders_AriaDisabled_Span_Not_Anchor()
	{
		var src = ReadAdminNavTree();

		// Disabled tiles render as <span aria-disabled="true"> with the muted class,
		// not as anchors. Pinned by T-03.3-C1 mitigation.
		Assert.Contains("aria-disabled=\"true\"", src);
		Assert.Contains("admin-nav-list__title--muted", src);
	}

	// --- AdminNavTree_Has_No_Drag_Drop_Attributes ---

	[Fact]
	public void AdminNavTree_Has_No_Drag_Drop_Attributes()
	{
		var src = ReadAdminNavTree();

		// Regression guard for D-09 — admin tree must NOT include any wiki-tree-specific
		// edit affordances; absence of .tv-del also confirms site.js drag/drop auto-skip.
		Assert.DoesNotContain("data-page-id", src);
		Assert.DoesNotContain("draggable=\"true\"", src);
		Assert.DoesNotContain("tv-del", src);
	}

	// --- AdminLayout_Wraps_RenderBody_In_admin_layout_Container ---

	[Fact]
	public void AdminLayout_Wraps_RenderBody_In_admin_layout_Container()
	{
		var src = ReadAdminLayout();

		Assert.Contains("class=\"admin-layout\"", src);
		Assert.Contains("class=\"admin-rail\"", src);
		Assert.Contains("class=\"admin-main\"", src);
	}

	// --- AdminLayout_Invokes_AdminNavTree_Partial ---

	[Fact]
	public void AdminLayout_Invokes_AdminNavTree_Partial()
	{
		var src = ReadAdminLayout();

		Assert.Contains("_AdminNavTree", src);
	}

	// --- AdminLayout_Main_Has_Id_main_content_For_Skip_Link ---

	[Fact]
	public void AdminLayout_Main_Has_Id_main_content_For_Skip_Link()
	{
		var src = ReadAdminLayout();

		Assert.Contains("id=\"main-content\"", src);
	}

	// --- SiteCss_Defines_admin_layout_Flex_Container ---

	[Fact]
	public void SiteCss_Defines_admin_layout_Flex_Container()
	{
		var src = ReadSiteCss();

		Assert.Contains(".admin-layout", src);
		Assert.Contains("display: flex", src);
		Assert.Contains("gap: 24px", src);
	}

	// --- SiteCss_Defines_admin_rail_Fixed_280_Width ---

	[Fact]
	public void SiteCss_Defines_admin_rail_Fixed_280_Width()
	{
		var src = ReadSiteCss();

		Assert.Contains(".admin-rail", src);
		Assert.Contains("280px", src);
	}

	// --- SiteCss_Has_Mobile_Breakpoint_640px ---

	[Fact]
	public void SiteCss_Has_Mobile_Breakpoint_640px()
	{
		var src = ReadSiteCss();

		Assert.Contains("@media (max-width: 640px)", src);
	}
}
