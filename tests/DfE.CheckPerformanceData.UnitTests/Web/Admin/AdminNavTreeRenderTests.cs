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

	private static string ReadAdminNavNode() =>
		File.ReadAllText(Path.Combine(SharedViewsDir, "_AdminNavNode.cshtml"));

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

	// --- AdminNavTree_Declares_Recursive_Node_Forest_Model ---

	[Fact]
	public void AdminNavTree_Declares_Recursive_Node_Forest_Model()
	{
		var src = ReadAdminNavTree();

		// The tree now renders a forest of recursive nodes rather than a flat 2-level model.
		Assert.Contains("@model", src);
		Assert.Contains("AdminNavNodeViewModel", src);
	}

	// --- AdminNavNode_Recurses_Into_Itself_For_Deep_Nesting ---

	[Fact]
	public void AdminNavNode_Recurses_Into_Itself_For_Deep_Nesting()
	{
		var src = ReadAdminNavNode();

		// The node partial must invoke itself so grandchildren / great-grandchildren render
		// (Rules Engine -> Queues -> Rules Engine Queue, etc.).
		Assert.Contains("_AdminNavNode", src);
		Assert.Contains("tv-children", src);
	}

	// --- AdminNavTree_Has_LeaveAdmin_Link_To_Home_At_Bottom ---

	[Fact]
	public void AdminNavTree_Has_LeaveAdmin_Link_To_Home_At_Bottom()
	{
		var src = ReadAdminNavTree();

		// A link at the foot of the tree that leaves the admin area for the service home page.
		Assert.Contains("admin-nav-tree__leave", src);
		Assert.Contains("href=\"/\"", src);
	}

	// --- AdminNavTree_Administration_Heading_Has_Distinct_Class ---

	[Fact]
	public void AdminNavTree_Administration_Heading_Has_Distinct_Class()
	{
		var src = ReadAdminNavTree();

		// The top "Administration" heading carries its own class so the CSS can size/weight it
		// above the group/sub-group labels beneath it.
		Assert.Contains("admin-nav-tree__heading", src);
	}

	// --- SiteCss_Defines_AdminNavTree_Heading_Larger_And_Bold ---

	[Fact]
	public void SiteCss_Defines_AdminNavTree_Heading_Larger_And_Bold()
	{
		var src = ReadSiteCss();

		Assert.Contains(".admin-nav-tree__heading", src);
		Assert.Contains("font-weight: 700", src);
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

	// --- AdminNavNode_PostEntry_Renders_Antiforgery_FormButton ---

	[Fact]
	public void AdminNavNode_PostEntry_Renders_Antiforgery_FormButton()
	{
		// A POST nav entry (e.g. "Seed sample pages") renders as an antiforgery-protected
		// form-button whose action is taken from the entry itself, rather than falling through
		// to the plain anchor (GET) branch or the muted aria-disabled span.
		var src = ReadAdminNavNode();

		Assert.Contains("HttpMethod == \"POST\"", src);
		Assert.Contains("AntiForgeryToken", src);
		Assert.Contains("action=\"@entry.Url\"", src);
	}

	// --- AdminNavTree_Disabled_Child_Renders_AriaDisabled_Span_Not_Anchor ---

	[Fact]
	public void AdminNavTree_Disabled_Child_Renders_AriaDisabled_Span_Not_Anchor()
	{
		// Disabled tiles render as <span aria-disabled="true"> with the muted class,
		// not as anchors. Pinned by T-03.3-C1 mitigation. The leaf markup now lives in
		// the recursive node partial.
		var src = ReadAdminNavNode();

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
