namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Reads the QualList qualification reference (AB#297848) from the rules-config container, blob
/// qualification-reference.json — the same arrangement as <see cref="IGradeReferenceClient"/>, and
/// deliberately a separate document: the two come from different teams on different cadences, and
/// merging them would couple the incorrect-grade validation contract to the QualList export.
/// The whole lookup is returned (not find-by-QAN) because the qualification search page needs the
/// AO list and each AO's QANs, not one record.
/// </summary>
public interface IQualificationReferenceClient
{
    Task<QualificationReferenceLookup> GetLookupAsync(CancellationToken ct = default);
}
