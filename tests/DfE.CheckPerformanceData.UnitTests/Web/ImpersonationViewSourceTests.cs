using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class ImpersonationViewSourceTests
{
    [Fact]
    public void Picker_explains_search_and_manual_entry_options()
    {
        var view = ReadView();

        Assert.Contains("enter the identifiers and age range directly", view);
        Assert.DoesNotContain("identifiers are used as entered", view);
        Assert.Contains("for=\"Laestab\"", view);
        Assert.Contains("for=\"Urn\"", view);
        Assert.Contains("for=\"LowestAge\"", view);
        Assert.Contains("for=\"HighestAge\"", view);
    }

    private static string ReadView()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));
        return File.ReadAllText(Path.Combine(root, "src", "DfE.CheckPerformanceData.Web", "Views", "Impersonation", "Index.cshtml"));
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
