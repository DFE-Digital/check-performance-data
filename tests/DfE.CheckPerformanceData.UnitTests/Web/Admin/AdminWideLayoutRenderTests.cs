namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// Static Razor/CSS/JS-source assertions for the Wave B full-width collapsible-nav
// template applied to the Pipeline dashboard and Debug pipeline pages only.
public sealed class AdminWideLayoutRenderTests
{
    private static string RepoRoot()
    {
        var thisFile = ThisFilePath();
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", ".."));
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static string ReadShared(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", name));

    private static string ReadView(string folder, string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", folder, name));

    private static string ReadCss(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "wwwroot", "css", name));

    private static string ReadJs(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", name));

    // --- WideLayout_File_Exists ---

    [Fact]
    public void WideLayout_File_Exists()
    {
        var path = Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "Shared", "_AdminWideLayout.cshtml");
        Assert.True(File.Exists(path), $"Expected wide layout at {path}");
    }

    // --- WideLayout_Opts_Into_Wide_Mode_Via_AdminLayout ---

    [Fact]
    public void WideLayout_Opts_Into_Wide_Mode_Via_AdminLayout()
    {
        var src = ReadShared("_AdminWideLayout.cshtml");

        // The wide layout delegates the admin chrome to _AdminLayout and flips the wide flag.
        Assert.Contains("_AdminLayout", src);
        Assert.Contains("AdminWide", src);
    }

    // --- AdminLayout_Renders_Collapse_Toggle_When_Wide ---

    [Fact]
    public void AdminLayout_Renders_Collapse_Toggle_When_Wide()
    {
        var src = ReadShared("_AdminLayout.cshtml");

        // The structural toggle (between rail and content) and the wide modifier live in
        // the shared admin layout, gated on the AdminWide flag.
        Assert.Contains("admin-layout--wide", src);
        Assert.Contains("admin-nav-collapse", src);
        Assert.Contains("data-admin-nav-collapse", src);
        Assert.Contains("aria-expanded", src);
    }

    // --- AdminLayout_Includes_The_Collapse_Script_When_Wide ---

    [Fact]
    public void AdminLayout_Includes_The_Collapse_Script_When_Wide()
    {
        var src = ReadShared("_AdminLayout.cshtml");

        Assert.Contains("admin-nav-collapse.js", src);
    }

    // --- Observability_Index_Uses_Wide_Layout ---

    [Fact]
    public void Observability_Index_Uses_Wide_Layout()
    {
        var src = ReadView("Observability", "Index.cshtml");

        Assert.Contains("_AdminWideLayout", src);
    }

    // --- DevUat_Index_Uses_Wide_Layout ---

    [Fact]
    public void DevUat_Index_Uses_Wide_Layout()
    {
        var src = ReadView("DevUat", "Index.cshtml");

        Assert.Contains("_AdminWideLayout", src);
    }

    // --- Css_Defines_Wide_Layout_And_Collapsed_State ---

    [Fact]
    public void Css_Defines_Wide_Layout_And_Collapsed_State()
    {
        var src = ReadCss("site.css");

        Assert.Contains(".admin-layout--wide", src);
        // A collapsed-nav modifier that hides the rail and lets content fill the row.
        Assert.Contains("admin-layout--nav-collapsed", src);
    }

    // --- CollapseScript_Exists_And_Uses_LocalStorage ---

    [Fact]
    public void CollapseScript_Exists_And_Uses_LocalStorage()
    {
        var path = Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", "admin-nav-collapse.js");
        Assert.True(File.Exists(path), $"Expected collapse script at {path}");

        var src = ReadJs("admin-nav-collapse.js");
        Assert.Contains("localStorage", src);
        Assert.Contains("aria-expanded", src);
    }
}
