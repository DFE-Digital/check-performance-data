namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Reads the AODC grade reference data (AB#297130) — the valid grades per qualification — from the
/// <c>rules-config</c> container, blob <c>grade-reference.json</c>. It sits alongside
/// <c>rules.json</c> because it is the same kind of thing: slow-moving reference data supplied by
/// another team, shared by every window, and self-seeded from a bundled copy.
/// </summary>
public interface IGradeReferenceClient
{
    /// <summary>
    /// The grades a qualification can award, or <c>null</c> when the QAN is absent from the
    /// reference data. Absent is a real state — the results CSVs and the AODC export come from
    /// different teams and can disagree — so callers must handle it rather than assume coverage.
    /// </summary>
    Task<GradeReference?> GetByQanAsync(string qan, CancellationToken ct = default);
}
