namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// Whether a pupil is included. Ingress decides it once and stamps <c>INCLUDED</c> on every pupil
/// record: from the slot's file of origin (16-19 included and non-included files), or else from the
/// schema's <c>x-ingress.inclusion</c> rule (KS4 and KS2, whose files carry each pupil's own code).
/// Readers read only the stamp, so no reader knows any key stage's codes.
/// </summary>
public static class PupilInclusion
{
    /// <summary>Inclusion for one raw record, as the exercise tabs read it. No stamp means not included.</summary>
    public static bool IsIncluded(IReadOnlyDictionary<string, string> row) =>
        row.TryGetValue("INCLUDED", out var stamp) && bool.TryParse(stamp, out var included) && included;

    /// <summary>
    /// The KS4 <c>P_INCL</c> codes that count as included. Only the Zendesk worker reads them, for
    /// its KS4 add-back removal reason; that is a ticket-routing rule, not the display's inclusion.
    /// </summary>
    public static readonly int[] Ks4IncludedPinclCodes = [401, 403, 414, 421, 431];

    /// <summary>Null / absent P_INCL means the flag was not supplied, treated as not included.</summary>
    public static bool IsKs4Included(int? pincl) => pincl is int code && Ks4IncludedPinclCodes.Contains(code);
}
