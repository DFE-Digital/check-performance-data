namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

// Source-text assertions pinning two defects found in the lead developer's PR review of
// _GradeSelect.cshtml (AB#296648).
//
// Defect A: aria-describedby was emitted via @(describedBy.Length > 0 ? $"aria-describedby=\"..." : "")
// — Razor HTML-encodes @(...) output, so the quotes became &quot; and the attribute never reached
// the browser as a real id reference. The hint, the "no grades" inset, and the error message were
// silently unreachable to screen readers. Fixed by using Razor's conditional attribute rendering
// (a null-valued attribute expression omits the attribute entirely) instead of building the whole
// "name=\"value\"" string inside an interpolated string.
//
// Defect B: the enhancement script ran as an IIFE that executes as soon as the browser parses it,
// mid-body. accessible-autocomplete.min.js loads at the bottom of _Layout.cshtml, after
// @RenderBody(), so typeof accessibleAutocomplete === 'undefined' was always true here and the
// guard always returned early — the grade picker never enhanced into the type-ahead. Fixed by
// wrapping the same body in a DOMContentLoaded listener, matching the pattern already used by
// _Autocomplete.cshtml.
public sealed class GradeSelectViewSourceTests
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

    private static string ViewSource() =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "_GradeSelect.cshtml"));

    [Fact]
    public void GradeSelect_RendersAriaDescribedByAsARealQuotedAttribute()
    {
        var view = ViewSource();

        // The conditional-attribute form: a bare @-expression as the attribute value, which Razor
        // omits entirely when null rather than encoding a hand-built "name=\"value\"" string.
        Assert.Contains("aria-describedby=\"@", view);
    }

    [Fact]
    public void GradeSelect_DoesNotBuildAriaDescribedByAsAnEncodedInterpolatedString()
    {
        var view = ViewSource();

        // The historic bug: building the whole attribute (name, =, and quotes) inside an
        // interpolated string inside @(...) causes Razor to HTML-encode the quotes as &quot;,
        // so the attribute never reaches the browser as a real id reference.
        Assert.DoesNotContain("aria-describedby=\\\"", view);
    }

    [Fact]
    public void GradeSelect_EnhancementRunsAfterDOMContentLoaded()
    {
        var view = ViewSource();

        // accessible-autocomplete.min.js loads at the bottom of _Layout.cshtml, after
        // @RenderBody(), so a script that runs immediately (an IIFE executing mid-parse) always
        // sees accessibleAutocomplete as undefined and bails out.
        Assert.Contains("document.addEventListener('DOMContentLoaded', function () {", view);
    }
}
