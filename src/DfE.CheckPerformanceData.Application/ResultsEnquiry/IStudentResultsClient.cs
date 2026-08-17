namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// Reads (and, for dev seeding, writes) the per-school 16-19 exam results held in blob storage at
/// container <c>{windowId}</c>, blob <c>results-enquiry/data/{laestab}_results.json</c> — see
/// <see cref="ResultsEnquiryBlobPaths"/>. The file is one merged array across all six supplier
/// input files, each row stamped with its <see cref="ResultsFileTags"/> source tag by ingestion.
/// </summary>
public interface IStudentResultsClient
{
    /// <summary>
    /// The results held for one student. Empty when the container, the blob or the student is
    /// absent — a school with no results is a normal state, not an error. Malformed JSON throws so
    /// a corrupt file surfaces rather than reading as "this student has no results".
    /// </summary>
    Task<IReadOnlyList<StudentResultRecord>> GetResultsAsync(Guid windowId, string laestab, string cypmdId, CancellationToken ct = default);

    /// <summary>
    /// Whether the school holds any result from a given source file. This is how the service works
    /// out for itself whether a supplier file has landed, rather than being told separately.
    /// </summary>
    Task<bool> AnyForSourceAsync(Guid windowId, string laestab, string sourceTag, CancellationToken ct = default);

    /// <summary>Writes a school's results file. Used only by development data seeding.</summary>
    Task UploadResultsAsync(Guid windowId, string laestab, IReadOnlyList<StudentResultRecord> results, CancellationToken ct = default);
}
