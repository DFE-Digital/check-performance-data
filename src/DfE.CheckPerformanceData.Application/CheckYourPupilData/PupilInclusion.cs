namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// Whether a pupil is included. Ingress decides it once and stamps <c>INCLUDED</c> on every pupil
/// record: from the slot's file of origin (16-19 included and non-included files), or else from the
/// schema's <c>x-ingress.inclusion</c> rule (KS4 and KS2, whose files carry each pupil's own code).
/// Readers read only the stamp, so no reader knows any key stage's codes.
/// A second stamp, <c>INCLUDABLE</c> (#711), says who a school may ask to include: ingress writes it
/// from the schema's <c>x-ingress.includable</c> rule, or as not-included where there is no rule.
/// </summary>
public static class PupilInclusion
{
    /// <summary>Inclusion for one raw record, as the exercise tabs read it. No stamp means not included.</summary>
    public static bool IsIncluded(IReadOnlyDictionary<string, string> row) =>
        row.TryGetValue("INCLUDED", out var stamp) && bool.TryParse(stamp, out var included) && included;
}
