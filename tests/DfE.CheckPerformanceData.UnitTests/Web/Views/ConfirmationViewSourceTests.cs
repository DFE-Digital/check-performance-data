using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// Source-file assertion pattern (mirrors LayoutViewSourceTests / WhatToChangeViewSourceTests):
// reads Views/Journey/Confirmation.cshtml from disk and pins the copy of the submission
// confirmation screen's CMS-editable "What happens next" block (issue #515 / AB#304140).
//
// Both defects live in the one `defaultHtml` literal for block key
// `journey_confirmation_next_steps`, so a source assertion is exact rather than approximate.
// The "no changes" check runs against *extracted visible copy*, not the raw file: the view
// also carries identifiers such as `asp-controller="WhatToChange"`, and the spec puts
// non-visible code on the screen out of scope.
public sealed class ConfirmationViewSourceTests
{
    private static string ReadViewSource(string relativePath)
    {
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web"));
        return File.ReadAllText(Path.Combine(viewsDir, relativePath));
    }

    // The copy a clerk actually sees on the screen: Razor comments, code blocks, directives,
    // markup (and so the attributes hanging off it) and bare @Model expressions are removed,
    // leaving the text nodes plus the string literals the component is handed
    // (defaultHtml / defaultText) — which is where this screen's copy lives.
    private static string VisibleCopy(string source)
    {
        var text = source;

        // @* ... *@ — prose for developers, never rendered.
        text = Regex.Replace(text, @"@[\*][\s\S]*?[\*]@", " ");

        // @{ ... } — ViewBag/Layout assignments, not page copy.
        text = Regex.Replace(text, @"@\{[\s\S]*?\}", " ");

        // @using / @model and friends: directive lines, never rendered.
        text = Regex.Replace(
            text,
            @"^\s*@(using|model|inject|addTagHelper|removeTagHelper|implements|inherits|layout|page)\b.*$",
            " ",
            RegexOptions.Multiline);

        // Markup plus everything inside the angle brackets, so attributes (asp-controller,
        // class, id ...) are out of scope by construction. The tags inside the defaultHtml
        // literal go too, leaving the words the block shows.
        text = Regex.Replace(text, "<[^>]*>", " ");

        // Bare Razor expressions left standing between tags (@Model.ReferenceNumber).
        text = Regex.Replace(text, @"@[A-Za-z_]\w*(\.\w+)*", " ");

        var literals = Regex.Matches(text, "\"((?:[^\"\\\\]|\\\\.)*)\"")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value);
        var betweenLiterals = Regex.Replace(text, "\"(?:[^\"\\\\]|\\\\.)*\"", " ");

        return string.Join(" ", literals) + " " + betweenLiterals;
    }

    [Fact]
    public void first_bullet_reads_review_any_evidence_not_and()
    {
        var source = ReadViewSource("Views/Journey/Confirmation.cshtml");

        Assert.Contains("the DfE will review any evidence you provide", source);
        Assert.DoesNotContain("the DfE will review and evidence you provide", source);
    }

    [Fact]
    public void screen_contains_no_user_visible_changes()
    {
        var source = ReadViewSource("Views/Journey/Confirmation.cshtml");

        Assert.DoesNotContain("changes", VisibleCopy(source), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void next_steps_lead_in_uses_amendments_not_changes()
    {
        var source = ReadViewSource("Views/Journey/Confirmation.cshtml");

        // Scoped to the lead-in rather than a blanket "changes" search over the file, which
        // would couple the test to identifiers like `asp-controller="WhatToChange"`.
        Assert.Contains("After you submit your amendments:", source);
        Assert.DoesNotContain("After you submit your changes:", source);
    }

    [Fact]
    public void unchanged_copy_stays_byte_identical()
    {
        var source = ReadViewSource("Views/Journey/Confirmation.cshtml");

        Assert.Contains("Data amendment request submitted", source);
        Assert.Contains(
            "We have sent you an email to confirm that you have successfully submitted a request.",
            source);
        Assert.Contains("You still have until", source);
        Assert.Contains("to request any further amendments if you need to.", source);
        Assert.Contains("Request another amendment", source);
        Assert.Contains("edit an existing request", source);
        Assert.Contains("What happens next", source);
        Assert.Contains("you'll be able to track the status of your requests in the service", source);
        Assert.Contains(
            "your school's performance data will be updated in the Autumn, if the request is approved",
            source);
    }
}
