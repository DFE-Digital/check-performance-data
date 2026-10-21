using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Source-file regression guard for #611. The CSP no longer carries 'unsafe-inline', so an inline
// event handler or a javascript: URL in a view is not merely poor practice - the browser blocks
// it and the page silently loses a behaviour. This mirrors ConfirmSweepTests: a text scan over
// every view, failing the build if a handler or URL comes back.
public sealed class CspSourceSweepTests
{
    // An event-handler attribute, not a JS property assignment: `input.onchange =` is inside a
    // <script> and is fine, so the match must not be preceded by a word character or a dot, and
    // must have a quoted value.
    private static readonly Regex InlineHandler = new(
        """(?<![\w.])on(?:click|change|submit|load|input|keyup|keydown|focus|blur|mouseover|mouseout)\s*=\s*["']""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JavascriptUrl = new(
        """href\s*=\s*["']\s*javascript:""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InlineConfirm = new(
        @"\breturn\s+confirm\s*\(",
        RegexOptions.Compiled);

    // The nonce is stamped centrally by CspNonceTagHelper. A literal attribute means someone
    // hard-coded a nonce, which will not match the response header.
    private static readonly Regex LiteralNonce = new(
        @"(?<![\w.])nonce\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The page template's script helpers emit <script> elements themselves, so the tag helper
    // never sees them. Called with no argument they emit no nonce at all and the browser blocks
    // them under the strict CSP; every call must be handed CspNonce.Get(Context).
    private static readonly Regex UnnoncedLibraryScript = new(
        @"GovUkFrontend(?:JsEnabledScript|ScriptImports)\s*\(\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void Sweep_HasViewsToScan()
    {
        Assert.NotEmpty(ViewFiles());
    }

    [Fact]
    public void NoView_HasAnInlineEventHandler()
    {
        AssertNoMatches(InlineHandler, "inline event-handler attribute");
    }

    [Fact]
    public void NoView_HasAJavascriptUrl()
    {
        AssertNoMatches(JavascriptUrl, "javascript: URL");
    }

    [Fact]
    public void NoView_HasAnInlineConfirmCall()
    {
        AssertNoMatches(InlineConfirm, "inline confirm() call");
    }

    [Fact]
    public void NoView_WritesANonceLiteral()
    {
        AssertNoMatches(LiteralNonce, "literal nonce attribute");
    }

    [Fact]
    public void NoView_CallsTheLibraryScriptHelpersWithoutANonce()
    {
        AssertNoMatches(UnnoncedLibraryScript, "nonce-less GovUkFrontend script helper call");
    }

    // US2: the analytics loaders inject a <script> at runtime. Under the strict CSP that element
    // is only allowed to run if it carries the request's nonce, so each loader must take the nonce
    // off the inline <script> that defines it (document.currentScript) - the element is long gone
    // by the time the loader runs from cookies.js.
    [Theory]
    [InlineData("Shared/_GtmHead.cshtml")]
    [InlineData("Shared/_ClarityHead.cshtml")]
    public void AnalyticsHead_PropagatesTheNonceToTheScriptItCreates(string relativePath)
    {
        var view = ReadView(relativePath);

        Assert.Contains("document.currentScript", view);
        Assert.Contains(".nonce", view);
        Assert.DoesNotContain("'unsafe-inline'", view);
        Assert.DoesNotContain("'unsafe-eval'", view);
    }

    private static string ReadView(string relativePath) =>
        File.ReadAllText(Path.Combine(
            FindSolutionRoot(AppContext.BaseDirectory),
            "src", "DfE.CheckPerformanceData.Web", "Views",
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static void AssertNoMatches(Regex pattern, string what)
    {
        var offenders = new List<string>();
        foreach (var file in ViewFiles())
        {
            var text = File.ReadAllText(file);
            var match = pattern.Match(text);
            if (match.Success)
            {
                offenders.Add($"{Relative(file)}:{LineNumber(text, match.Index)} -> {Snippet(text, match.Index)}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Found {offenders.Count} {what}(s); under the strict CSP these are blocked by the browser:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> ViewFiles()
    {
        var viewsRoot = Path.Combine(
            FindSolutionRoot(AppContext.BaseDirectory),
            "src", "DfE.CheckPerformanceData.Web", "Views");
        return Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories);
    }

    private static string Relative(string fullPath)
    {
        var marker = $"Views{Path.DirectorySeparatorChar}";
        var index = fullPath.LastIndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? Path.GetFileName(fullPath) : fullPath[(index + marker.Length)..];
    }

    private static int LineNumber(string text, int index) =>
        text.AsSpan(0, index).Count('\n') + 1;

    private static string Snippet(string text, int index)
    {
        var end = text.IndexOf('\n', index);
        if (end < 0)
        {
            end = text.Length;
        }

        return text[index..end].Trim();
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
