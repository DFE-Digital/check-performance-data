namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// AB#297130: the valid grades for one qualification, from the AODC reference data.
/// <see cref="PassGrades"/> render before <see cref="FailGrades"/>; ordering within each list is
/// preserved from the source, because a grade scale has a meaningful order (highest first) that
/// alphabetical sorting would destroy.
/// </summary>
public sealed class GradeReference
{
    public required string Qan { get; init; }
    public required string QualificationTitle { get; init; }
    public string AwardingOrganisation { get; init; } = string.Empty;
    public required IReadOnlyList<string> PassGrades { get; init; }
    public required IReadOnlyList<string> FailGrades { get; init; }

    /// <summary>The full picker option order: every pass grade, then every fail grade.</summary>
    public IReadOnlyList<string> AllGrades => [.. PassGrades, .. FailGrades];

    /// <summary>
    /// Whether a posted grade is one this qualification actually offers. Ordinal and
    /// case-sensitive: grades such as <c>24F</c> and <c>24D</c> differ only by suffix, and a
    /// case-insensitive match could accept a value the picker never rendered.
    /// </summary>
    public bool Offers(string? grade)
        => grade is not null && AllGrades.Contains(grade, StringComparer.Ordinal);
}
