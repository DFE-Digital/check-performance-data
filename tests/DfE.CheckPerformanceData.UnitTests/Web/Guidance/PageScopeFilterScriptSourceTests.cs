namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Guidance;

// The page picker filter is hand-written JavaScript with no runner, so these read it as text and
// pin the two rules that matter: it only ever hides things (a hidden ticked page must still post),
// and every picker on the editor page is filtered on its own.
public sealed class PageScopeFilterScriptSourceTests
{
    private static readonly string Script = ReadScript();

    [Fact]
    public void Script_NeverDisablesOrRemovesCheckboxes()
    {
        Assert.DoesNotContain(".disabled", Script);
        Assert.DoesNotContain("removeChild", Script);
        Assert.DoesNotContain(".remove(", Script);
    }

    [Fact]
    public void Script_ScopesEachPickerSeparately()
    {
        Assert.Contains("querySelectorAll('[data-cpb-scope-picker]')", Script);
    }

    private static string ReadScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js", "cms-page-scope-filter.js");
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("cms-page-scope-filter.js not found above " + AppContext.BaseDirectory);
    }
}
