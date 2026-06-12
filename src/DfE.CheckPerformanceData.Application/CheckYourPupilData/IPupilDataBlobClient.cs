namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// Reads (and, for dev seeding, writes) the per-school pupil JSON held in blob storage at
/// container <c>{windowId}</c>, blob <c>data/{laestab}_pupils.json</c>. The <c>/</c> in a
/// laestab is stripped when forming the blob name.
/// </summary>
public interface IPupilDataBlobClient
{
    /// <summary>
    /// Returns the school's pupils for a window, or <c>null</c> when the container or blob
    /// does not exist. Malformed JSON is allowed to throw.
    /// </summary>
    Task<IReadOnlyList<PupilRecord>?> GetPupilsAsync(Guid windowId, string laestab);

    /// <summary>Cheap existence check used by the landing page to populate HasPupilData.</summary>
    Task<bool> HasPupilDataAsync(Guid windowId, string laestab);

    /// <summary>Writes a school's pupil file. Used only by development data seeding.</summary>
    Task UploadPupilsAsync(Guid windowId, string laestab, IReadOnlyList<PupilRecord> pupils);
}
