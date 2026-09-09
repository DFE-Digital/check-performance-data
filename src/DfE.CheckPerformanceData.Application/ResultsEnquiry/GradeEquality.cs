namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Whether a grade is the grade a result already holds. Ordinal and case-sensitive throughout:
/// grades are opaque codes, and the IB Diploma proves why precision matters — 24F is a fail and
/// 24D a pass, so 24F → 24D is a real change. Any normalising comparison risks either rejecting a
/// genuine enquiry or accepting a no-op one. Surrounding whitespace is ignored because a posted
/// form value may carry it and the supplier CSV may too.
///
/// This is the single definition shared by the revised-grade validator (refuses a posted no-op)
/// and the picker (never offers the current grade in the first place). AB#296648, AB#301913.
/// </summary>
public static class GradeEquality
{
    public static bool IsSame(string? candidate, string? current)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(current))
            return false;

        return string.Equals(candidate.Trim(), current.Trim(), StringComparison.Ordinal);
    }
}
