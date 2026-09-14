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
// Defect B (historic, now moot): the enhancement script ran as an IIFE mid-body, before
// accessible-autocomplete.min.js loaded at the bottom of _Layout.cshtml, so the guard always
// returned early. It was fixed with a DOMContentLoaded listener, and the enhancement has since been
// removed altogether — the grade list is short enough to read, so the picker is a plain dropdown.
// ResultDetailsViewSourceTests pins that there is no script left.
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
}
