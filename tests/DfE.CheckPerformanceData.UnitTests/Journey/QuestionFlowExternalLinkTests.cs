using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// AB#286387 QA fix: "could you also add this at the end of the gias link & any other
// external links: ?utm_source=cypmd&utm_medium=referral&utm_campaign=help_guidance".
// The only external links in authored content are the 4 GIAS links in the seeded
// question-flow JSON (Remove_KS4June.json). This guard test pins that every GIAS link
// occurrence carries the UTM query string, using &amp; because questionHelpText is raw
// HTML rendered via @Html.Raw (Views/Journey/_Question.cshtml:29) — a bare & would be
// invalid in an HTML attribute.
public sealed class QuestionFlowExternalLinkTests
{
    private static string RepoRoot
    {
        get
        {
            var thisFile = ThisFilePath();
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        }
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    private static string PathToRepoFile(string relativePath) =>
        Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Gias_links_carry_utm_parameters()
    {
        var json = File.ReadAllText(PathToRepoFile(
            "src/DfE.CheckPerformanceData.Web/Data/QuestionFlows/Remove_KS4June.json"));

        var giasCount = Regex.Matches(json, "get-information-schools\\.service\\.gov\\.uk").Count;
        var utmCount = Regex.Matches(json,
            "get-information-schools\\.service\\.gov\\.uk/\\?utm_source=cypmd&amp;utm_medium=referral&amp;utm_campaign=help_guidance").Count;

        Assert.True(giasCount > 0, "expected GIAS links in the question flow");
        Assert.Equal(giasCount, utmCount); // every GIAS link carries the UTM params
    }
}
