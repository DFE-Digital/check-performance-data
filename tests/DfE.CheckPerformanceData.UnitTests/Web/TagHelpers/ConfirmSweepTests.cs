namespace DfE.CheckPerformanceData.Application.UnitTests.Web.TagHelpers;

// Source-file regression guard. The 3 destructive-action confirmations across the wiki and
// content-block surfaces must be served by the GovukConfirmModalTagHelper, never by inline
// browser-native confirm() calls. If a future view edit re-introduces confirm(), this fails.
public sealed class ConfirmSweepTests
{
    // --- AllInlineConfirmCalls_AreSweptFromViews ---

    [Fact]
    public void HelpWikiTree_HasNoInlineConfirmCall()
    {
        var view = ReadView("Help/_WikiTree.cshtml");
        Assert.DoesNotContain("onclick=\"return confirm(", view);
    }

    [Fact]
    public void HelpVersions_HasNoInlineConfirmCall()
    {
        var view = ReadView("Help/Versions.cshtml");
        Assert.DoesNotContain("onclick=\"return confirm(", view);
    }

    [Fact]
    public void ContentBlockVersions_HasNoInlineConfirmCall()
    {
        var view = ReadView("ContentBlock/Versions.cshtml");
        Assert.DoesNotContain("onclick=\"return confirm(", view);
    }

    // --- AllSweptViews_InvokeGovukConfirmModalTagHelper ---

    [Fact]
    public void HelpWikiTree_InvokesConfirmModalTagHelper()
    {
        var view = ReadView("Help/_WikiTree.cshtml");
        Assert.Contains("<govuk-confirm-modal", view);
    }

    [Fact]
    public void HelpVersions_InvokesConfirmModalTagHelper()
    {
        var view = ReadView("Help/Versions.cshtml");
        Assert.Contains("<govuk-confirm-modal", view);
    }

    [Fact]
    public void ContentBlockVersions_InvokesConfirmModalTagHelper()
    {
        var view = ReadView("ContentBlock/Versions.cshtml");
        Assert.Contains("<govuk-confirm-modal", view);
    }

    // --- helpers ---

    private static string ReadView(string relativePath)
    {
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        var fullPath = Path.Combine(
            solutionRoot,
            "src", "DfE.CheckPerformanceData.Web", "Views",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(fullPath);
    }

    private static string FindSolutionRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetDirectories("src").Length > 0)
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate solution root from {startDir} (no .slnx or src/ found in any ancestor).");
    }
}
