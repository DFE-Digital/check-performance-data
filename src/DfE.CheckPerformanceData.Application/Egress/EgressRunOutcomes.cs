using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The one place that groups the eight pipeline statuses into the four the runs history shows
/// (AB#294590). Of() has no catch-all for a known status on purpose: a new EgressRunStatus member
/// must be placed here, or the history throws rather than filing it under a guess.
/// </summary>
public static class EgressRunOutcomes
{
    /// <summary>The status filter's option order.</summary>
    public static readonly IReadOnlyList<EgressRunOutcome> All =
        [EgressRunOutcome.Success, EgressRunOutcome.Failed, EgressRunOutcome.Draft, EgressRunOutcome.Abandoned];

    public static EgressRunOutcome Of(EgressRunStatus status) => status switch
    {
        EgressRunStatus.Transferred => EgressRunOutcome.Success,
        EgressRunStatus.PreprocessingFailed or EgressRunStatus.TransferFailed => EgressRunOutcome.Failed,
        EgressRunStatus.Pulled or EgressRunStatus.Preprocessing or EgressRunStatus.Preprocessed or EgressRunStatus.Transferring => EgressRunOutcome.Draft,
        EgressRunStatus.Abandoned => EgressRunOutcome.Abandoned,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Every EgressRunStatus must be placed in an EgressRunOutcome (runs history, AB#294590).")
    };

    /// <summary>The statuses the history query selects for one outcome — the exact inverse of Of().</summary>
    public static IReadOnlyList<EgressRunStatus> StatusesOf(EgressRunOutcome outcome) =>
        Enum.GetValues<EgressRunStatus>().Where(s => Of(s) == outcome).ToList();

    // FLAGGED copy (AB#294590).
    public static string Label(EgressRunOutcome outcome) => outcome switch
    {
        EgressRunOutcome.Success => "Success",
        EgressRunOutcome.Failed => "Failed",
        EgressRunOutcome.Draft => "Draft",
        EgressRunOutcome.Abandoned => "Abandoned",
        _ => outcome.ToString()
    };

    /// <summary>
    /// The ?status= query value: an outcome name in any casing, or null (no filter) for anything
    /// else. Matched by name rather than Enum.TryParse so "2" cannot select Draft.
    /// </summary>
    public static EgressRunOutcome? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var wanted = value.Trim();
        foreach (var outcome in All)
            if (string.Equals(outcome.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
                return outcome;
        return null;
    }
}
