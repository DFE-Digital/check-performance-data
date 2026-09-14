namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Reads the QualList 16-19 qualification reference (AB#297848) from the rules-config container,
/// blob qualification-reference.json — beside rules.json because it is the same kind of thing:
/// slow-moving reference data from another team, shared by every window, self-seeded from a bundled
/// copy. Since AB#301903 it is the single source of qualification titles, awarding organisations and
/// grade scales for every results-enquiry journey. The whole lookup is returned (not find-by-QAN)
/// because the qualification search page needs the AO list and each AO's QANs, not one record.
/// </summary>
public interface IQualificationReferenceClient
{
    Task<QualificationReferenceLookup> GetLookupAsync(CancellationToken ct = default);
}
