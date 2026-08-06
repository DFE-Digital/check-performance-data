namespace DfE.CheckPerformanceData.Web.Common;

/// <summary>
/// Builds the document title shown in the browser tab and announced by screen readers on
/// page load.
/// </summary>
/// <remarks>
/// When a submission fails validation the GOV.UK Design System requires the title to be
/// prefixed with "Error: ". The title is the first thing a screen reader announces after a
/// navigation, so without the prefix there is nothing to signal that the page came back with
/// errors rather than moving on — the user only finds out when they reach the error summary.
/// </remarks>
public static class PageTitle
{
    public const string ErrorPrefix = "Error: ";

    /// <summary>
    /// Returns <paramref name="title"/> prefixed with "Error: " when <paramref name="hasError"/>
    /// is true. Idempotent: a title that already carries the prefix is returned unchanged, so
    /// applying this in a layout cannot double up on a view that has already prefixed its own
    /// title. A null or blank title is returned as-is — a bare "Error:" names no page.
    /// </summary>
    public static string? WithErrorPrefix(string? title, bool hasError)
    {
        if (!hasError || string.IsNullOrWhiteSpace(title)) return title;
        return title.StartsWith(ErrorPrefix, StringComparison.Ordinal) ? title : ErrorPrefix + title;
    }
}
