using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Web.Controllers;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// Pins how PupilSearch.cshtml asks for a restricted search and how it explains one.
///
/// A results enquiry lists only students the school holds a result for. That hides students, so
/// the page has to say so — otherwise a school cannot tell a typo from "this student has no
/// results". The hint comes from the flow config; the no-match wording is the view's own, because
/// accessible-autocomplete renders it.
/// </summary>
public sealed class PupilSearchViewSourceTests
{
    private static string ViewSource() =>
        File.ReadAllText(Path.Combine(
            RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Journey", "PupilSearch.cshtml"));

    [Fact]
    public void The_suggestions_request_carries_the_pages_results_restriction()
    {
        var view = ViewSource();

        Assert.Contains("var requireResults = @(Model.RequireResults ? \"true\" : \"false\");", view);
        Assert.Contains("if (requireResults) url += '&requireResults=true';", view);
    }

    [Fact]
    public void A_restricted_search_says_why_a_student_may_be_missing_when_nothing_matches()
    {
        // The component's default is a bare "No results found", which reads as "you typed it wrong".
        var view = ViewSource();

        Assert.Contains("tNoResults:", view);
        Assert.Contains("No students found with results", view);
    }

    [Fact]
    public void An_unrestricted_search_keeps_the_standard_no_results_wording()
    {
        // The KS4 journeys search the whole roll, where the only reason for no match is the query.
        Assert.Contains("'No results found'", ViewSource());
    }

    [Fact]
    public void The_suggestions_request_carries_the_pages_search_field()
    {
        // A page whose label names one identifier sends it, so the endpoint narrows its matching.
        var view = ViewSource();

        Assert.Contains("var searchField = '@Model.SearchField';", view);
        Assert.Contains("if (searchField !== 'All') url += '&pupilSearchField=' + searchField;", view);
    }

    [Fact]
    public void An_unrestricted_search_omits_the_search_field_entirely()
    {
        // Sent only when the page config asks for it. The endpoint defaults to All, so omitting it
        // is what keeps every unconfigured page — Remove, Include, the merge first record, all of
        // 16-19 — matching exactly as it did before the parameter existed.
        var view = ViewSource();

        Assert.Contains("if (searchField !== 'All')", view);
        Assert.DoesNotContain("pupilSearchField=' + searchField + '&", view);
    }

    [Fact]
    public void Every_query_parameter_the_view_sends_binds_to_a_parameter_of_the_suggestions_action()
    {
        // Model binding is by name and silently ignores a query key the action does not declare.
        // When this view sent `pupilSearchField` to an action declaring `searchField`, the
        // restriction never arrived: the page fell back to the `All` default and searched by name
        // again, with no error anywhere — and both halves still had passing tests, because the
        // view's test asserted the string it emits and the endpoint's tests called the service
        // directly. Reflecting the action's parameters is the only assertion spanning both halves.
        var sent = QueryKeysSent(ViewSource());

        var declared = typeof(PupilSuggestionsController)
            .GetMethod(nameof(PupilSuggestionsController.Suggestions))!
            .GetParameters()
            .Select(p => p.Name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in sent)
            Assert.Contains(key, declared);
    }

    [Fact]
    public void The_view_sends_the_search_restrictions_the_endpoint_expects()
    {
        // Pins the key set itself, so a parameter cannot be dropped from the request or renamed on
        // one side alone: the binding test above would pass on an empty list, and this fails if the
        // emitted keys change without the endpoint gaining the matching parameter.
        Assert.Equal(
            new[] { "excludePupilId", "filter", "pupilSearchField", "query", "requireResults", "windowId" },
            QueryKeysSent(ViewSource()));
    }

    /// <summary>
    /// The query keys the view appends to /pupils/suggestions, read off the `?key=` / `&key=`
    /// fragments of the URL it builds. Sorted so the comparison does not depend on the order the
    /// view happens to concatenate them in.
    /// </summary>
    private static string[] QueryKeysSent(string view) =>
        Regex.Matches(view, @"[?&]([A-Za-z][A-Za-z0-9]*)=")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

    private static string RepoRoot
    {
        get
        {
            var thisFile = ThisFilePath();
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        }
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
