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

    /// <summary>
    /// Inclusion for one raw record, as the exercise tabs read it. An <c>INCLUDED</c> stamp wins,
    /// because a slot's file of origin placed it; otherwise the KS4 <c>P_INCL</c> code decides.
    /// A record with neither is not included, the same as a null <c>P_INCL</c>.
    /// </summary>
    public static bool IsIncluded(IReadOnlyDictionary<string, string> row)
    {
        if (row.TryGetValue("INCLUDED", out var stamp) && bool.TryParse(stamp, out var included))
            return included;
        return row.TryGetValue("P_INCL", out var pincl) && int.TryParse(pincl, out var code)
            && IsKs4Included(code);
    }

    /// <summary>
    /// The school-facing words for a KS4 <c>P_INCL</c> code, as the CSV's "Pupil Inclusion
    /// description" column shows them. Ingress stamps them into <c>P_INCL_DESC</c>, because the
    /// supplier file does not carry that column. An unknown or null code has no description.
    /// </summary>
    public static string Ks4Description(int? pincl) =>
        pincl is int code ? Ks4PinclDescriptions.GetValueOrDefault(code, string.Empty) : string.Empty;

    private static readonly Dictionary<int, string> Ks4PinclDescriptions = new()
    {
        { 401, "Pupil on roll and included in key stage 4 (both NOR and results)." },
        { 403, "Pupil added back. Included in key stage 4 (both NOR and results)." },
        { 414, "Year group adjusted to 11. Pupil reported as Year 10 or below last year. Included in key stage 4 NOR and results." },
        { 421, "Pupil assumed to be on your roll. Included in key stage 4 data." },
        { 431, "Year group adjusted to 11. Pupil reported as Year 10 or below last year. Included in key stage 4 NOR and results." },
        { 402, "Pupil not on roll and omitted from all figures to be published." },
        { 404, "Pupil on roll. Pupil is not at the end of key stage 4 and is omitted from key stage 4 data." },
        { 407, "Pupil admitted following permanent exclusion from a maintained school. Omitted from NOR and results data." },
        { 408, "Pupil on roll. Pupil aged 15 admitted following permanent exclusion from a maintained school. Omitted from key stage 4." },
        { 410, "Pupil on roll but dual-registered with another school and is published elsewhere." },
        { 413, "Year group adjusted to 12. Pupil previously reported as end of key stage 4. Omitted from all data." },
        { 422, "Pupil assumed to be on your roll. Pupil is estimated not to be in year 11. Omitted from key stage 4." },
        { 430, "Year group adjusted to 12. Pupil previously reported as end of key stage 4. Omitted from all data." },
    };
}
