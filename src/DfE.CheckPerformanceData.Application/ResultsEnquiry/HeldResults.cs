namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>The copies of one result that a student holds in other results files.</summary>
public static class HeldResults
{
    /// <summary>
    /// Returns the student's results with the qualification and session of
    /// <paramref name="selected"/>, without <paramref name="selected"/> itself, in the order given.
    /// </summary>
    public static IReadOnlyList<ResultRecord> InOtherFiles(
        ResultRecord selected, IEnumerable<ResultRecord> studentResults)
    {
        var subject = EnquirySubject.ForResult(selected);
        return [.. studentResults.Where(r =>
            !string.Equals(r.CompositeKey, selected.CompositeKey, StringComparison.Ordinal)
            && EnquirySubject.ForResult(r) == subject)];
    }
}
