using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.UnitTests.SiteAssets;

public sealed class SiteAssetsAdminViewTests
{
    private static readonly string View = File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Views", "Admin", "SiteAssets", "Index.cshtml"));

    [Theory]
    [InlineData("css")]
    [InlineData("js")]
    public void Editor_IsALargeMultiLineTextarea_InMonospace(string id)
    {
        var match = Regex.Match(View, $"<textarea[^>]*id=\"{id}\"[^>]*>", RegexOptions.Singleline);

        Assert.True(match.Success, $"expected a <textarea id=\"{id}\">");
        Assert.Contains("govuk-textarea", match.Value);
        Assert.Contains("admin-code-textarea", match.Value);
        var rows = int.Parse(Regex.Match(match.Value, "rows=\"([0-9]+)\"").Groups[1].Value);
        Assert.True(rows >= 10, $"rows was {rows}");
    }

    [Fact]
    public void Page_MentionsTheSafeModeSwitch()
    {
        Assert.Contains("?siteAssets=off", View);
    }

    [Fact]
    public void Page_UsesNoRichTextEditor()
    {
        Assert.DoesNotContain("tinymce", View, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
