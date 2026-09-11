namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The grade scale the revised-grade and missing-grade pickers render and validate against. Since
/// AB#301903 it is only ever produced by <see cref="QualificationReference.ToGradeReference"/> from
/// the 16-19 qualification reference — every grade lands in <see cref="PassGrades"/>,
/// <see cref="FailGrades"/> is empty, and the order is the reference's own (a scale's order is
/// meaningful; alphabetical sorting would destroy it). The pass/fail split is kept because the
/// validator and picker consume <see cref="AllGrades"/>, and a future source may carry the split.
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
