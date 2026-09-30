using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// AB#306103: every school-facing link to the guidance opens it in a new tab and says so in its
// visible link text. Three anchors exist — the service-navigation and footer links in _Layout and
// the "Guidance" card on the home page — and these facts pin all three together so one cannot
// drift from the others. Hostless Razor-source assertions, the PhaseBannerFeedbackLinkTests pattern.
public sealed class GuidanceLinksViewSourceTests
{
    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    // Repo root is four levels up from tests/DfE.CheckPerformanceData.UnitTests/Web/Views/.
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string View(string folder, string name) => File.ReadAllText(Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", folder, name));

    // Every <a …>…</a> whose href is exactly /guidance, from "<a " to "</a>".
    private static IReadOnlyList<string> GuidanceAnchors(string view)
    {
        var anchors = new List<string>();
        var from = 0;
        while (true)
        {
            var href = view.IndexOf("href=\"/guidance\"", from, StringComparison.Ordinal);
            if (href < 0) break;
            var starts = Regex.Matches(view[..href], @"<a\s");
            var start = starts.Count == 0 ? -1 : starts[^1].Index;
            var end = view.IndexOf("</a>", href, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > href, "a /guidance anchor is not delimited by <a … </a>");
            // The footer anchors are written over three lines in the GOV.UK style, so whitespace
            // inside the tag and around the text is collapsed before asserting.
            var anchor = Regex.Replace(view[start..(end + "</a>".Length)], @"\s+", " ")
                .Replace("> ", ">", StringComparison.Ordinal)
                .Replace(" </a>", "</a>", StringComparison.Ordinal);
            anchors.Add(anchor);
            from = end;
        }
        return anchors;
    }

    public static TheoryData<string, string, int, string> Sites => new()
    {
        // folder, file, expected anchor count, expected visible link text
        { "Shared", "_Layout.cshtml", 2, "Guidance (opens in new tab)" },
        { "Home", "Index.cshtml", 1, "How to check your performance data (opens in new tab)" },
    };

    [Theory]
    [MemberData(nameof(Sites))]
    public void Every_guidance_link_opens_in_a_new_tab(string folder, string name, int count, string text)
    {
        var anchors = GuidanceAnchors(View(folder, name));

        // The count is pinned so a fourth guidance link cannot appear without either opening in
        // a new tab (and being counted here) or failing this fact.
        Assert.Equal(count, anchors.Count);
        Assert.All(anchors, a => Assert.Contains("target=\"_blank\"", a));
        _ = text;
    }

    [Theory]
    [MemberData(nameof(Sites))]
    public void Every_guidance_link_says_so_in_its_visible_text(string folder, string name, int count, string text)
    {
        // GOV.UK Design System links guidance: a link that opens in a new tab says so in its
        // link text, visibly — not in a visually-hidden span.
        var anchors = GuidanceAnchors(View(folder, name));

        Assert.Equal(count, anchors.Count);
        Assert.All(anchors, a => Assert.EndsWith(">" + text + "</a>", a));
    }

    [Theory]
    [MemberData(nameof(Sites))]
    public void Every_guidance_link_uses_noopener_but_keeps_the_referer(string folder, string name, int count, string text)
    {
        // rel="noopener" closes the window.opener hole. "noreferrer" is deliberately absent:
        // /guidance is same-origin, and DfE Analytics records RequestReferer on every page view,
        // so noreferrer would blank where each guidance visit came from (the AB#301012 precedent).
        var anchors = GuidanceAnchors(View(folder, name));

        Assert.Equal(count, anchors.Count);
        Assert.All(anchors, a => Assert.Contains("rel=\"noopener\"", a));
        Assert.All(anchors, a => Assert.DoesNotContain("noreferrer", a));
        _ = text;
    }
}
