namespace DfE.CheckPerformanceData.Application.RequestSubmission;

/// <summary>
/// Reads the year group that a KS4 "year group change" removal moves the pupil to. Egress and
/// the Zendesk ticket both send it.
/// </summary>
public static class YearGroupChange
{
    /// <summary>
    /// Returns the year group that the school chose, or null when the request is not a year
    /// group change. The KS4 Remove journey asks higher-lower, then year-group-higher-moved-to
    /// (12/13) or year-group-lower-moved-to (8/9/10). A stale answer from the other branch can
    /// stay in the answers, so the branch that the school chose decides which answer is read.
    /// </summary>
    /// <param name="answer">Gets an answer's raw value by question id.</param>
    public static string? MovedTo(Func<string, string?> answer)
    {
        if (!string.Equals(answer("reason"), "year-group-change", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var lower = string.Equals(answer("higher-lower"), "lower", StringComparison.OrdinalIgnoreCase);
        return lower ? answer("year-group-lower-moved-to") : answer("year-group-higher-moved-to");
    }
}
