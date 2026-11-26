namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Reads the qualification reference documents of the results enquiry journeys. Each document is a
/// blob in the rules-config container.
/// </summary>
public interface IQualificationReferenceClient
{
    /// <summary>Gets the whole lookup of one reference document.</summary>
    /// <param name="set">The document to read.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The lookup. <see cref="QualificationReferenceLookup.Empty"/> when the document is not in storage.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value of <paramref name="set"/> is not a defined document.</exception>
    /// <exception cref="System.Text.Json.JsonException">The document is not valid JSON.</exception>
    /// <remarks>A storage failure other than a missing document is not caught. It reaches the caller.</remarks>
    Task<QualificationReferenceLookup> GetLookupAsync(QualificationReferenceSet set, CancellationToken ct = default);
}
