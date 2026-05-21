namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

/// <summary>
/// Source-file assertions guarding the removal of the legacy "Seed sample pages"
/// form from the wiki edit-mode toolbar. The seeding affordance now lives as an
/// admin tile (Plan 02); this regression guard prevents a future refactor from
/// silently re-adding the form to Views/Help/Index.cshtml.
/// </summary>
public sealed class SeedSamplePagesMovedTests
{
    private static string ReadHelpIndexCshtml()
    {
        // Walk from AppContext.BaseDirectory up to the repo root (marked by the
        // solution file) and locate the view file relative to that root.
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null
            && !File.Exists(Path.Combine(current.FullName, "DfE.CheckPerformanceData.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException(
                "Could not locate DfE.CheckPerformanceData.sln to resolve repo root.");
        }

        var path = Path.Combine(
            current.FullName,
            "src",
            "DfE.CheckPerformanceData.Web",
            "Views",
            "Help",
            "Index.cshtml");

        return File.ReadAllText(path);
    }

    // --- HelpIndex_DoesNotContain_WikiModeSeedForm_Class ---

    [Fact]
    public void HelpIndex_DoesNotContain_WikiModeSeedForm_Class()
    {
        var src = ReadHelpIndexCshtml();

        Assert.DoesNotContain("wiki-mode-seed-form", src);
    }

    // --- HelpIndex_DoesNotContain_Seed_AspAction ---

    [Fact]
    public void HelpIndex_DoesNotContain_Seed_AspAction()
    {
        var src = ReadHelpIndexCshtml();

        Assert.DoesNotContain("asp-action=\"Seed\"", src);
    }

    // --- HelpIndex_DoesNotContain_SeedSamplePages_ButtonLabel ---

    [Fact]
    public void HelpIndex_DoesNotContain_SeedSamplePages_ButtonLabel()
    {
        var src = ReadHelpIndexCshtml();

        Assert.DoesNotContain("Seed sample pages", src);
    }

    // --- HelpIndex_StillContains_ViewMode_And_DeletedPages_Links ---

    [Fact]
    public void HelpIndex_StillContains_ViewMode_And_DeletedPages_Links()
    {
        var src = ReadHelpIndexCshtml();

        // The two surviving toolbar items in edit-mode must remain after the
        // seed-form removal. The .wiki-mode-link class is the anchor styling
        // shared by both surviving links.
        Assert.Contains("wiki-mode-link", src);
        Assert.Contains("View mode", src);
        Assert.Contains("Deleted pages", src);
    }
}
