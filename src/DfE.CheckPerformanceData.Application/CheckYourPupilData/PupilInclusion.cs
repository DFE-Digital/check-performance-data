namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// The single source of truth for which KS4 <c>P_INCL</c> codes count as "included".
/// Post16 does not use codes at all — its non-included supplier file has no <c>P_INCL</c>
/// column, so inclusion is stamped at ingest from the file of origin (see
/// <see cref="Post16PupilRecord.Included"/>).
/// </summary>
public static class PupilInclusion
{
    public static readonly int[] Ks4IncludedPinclCodes = [401, 403, 414, 421, 431];

    /// <summary>Null / absent P_INCL means the flag was not supplied, treated as not included.</summary>
    public static bool IsKs4Included(int? pincl) => pincl is int code && Ks4IncludedPinclCodes.Contains(code);
}
