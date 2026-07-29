using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// Reads (and, for dev seeding, writes) the per-school pupil JSON held in blob storage at
/// container <c>{windowId}</c>, blob <c>data/{laestab}_pupils.json</c>. The <c>/</c> in a
/// laestab is stripped when forming the blob name.
///
/// The blob holds a different record shape per window type (KS4 vs Post16), so reads and writes
/// take the window type and the caller sees only <see cref="IPupilRecord"/>. The container and
/// blob naming are identical for every window type — for Post16, ingress merges the supplier's
/// two CSVs into this one file per school.
/// </summary>
public interface IPupilDataBlobClient
{
    /// <summary>
    /// Returns the school's pupils for a window, or <c>null</c> when the container or blob
    /// does not exist. Malformed JSON is allowed to throw.
    /// </summary>
    Task<IReadOnlyList<IPupilRecord>?> GetPupilsAsync(Guid windowId, string laestab, CheckingWindowType windowType);

    /// <summary>Cheap existence check used by the landing page to populate HasPupilData.</summary>
    Task<bool> HasPupilDataAsync(Guid windowId, string laestab);

    /// <summary>Writes a school's pupil file. Used only by development data seeding.</summary>
    Task UploadPupilsAsync<T>(Guid windowId, string laestab, List<T> pupils) where T : IPupilRecord;
}
