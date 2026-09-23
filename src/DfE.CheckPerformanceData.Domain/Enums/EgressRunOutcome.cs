namespace DfE.CheckPerformanceData.Domain.Enums;

/// <summary>
/// How the egress runs history (AB#294590) reports a run: the ticket's Success / Failed / Draft,
/// plus Abandoned, which the pipeline produces and the history must not hide (decision 21 Sep 2026).
/// The grouping from <see cref="EgressRunStatus"/> lives in Application (EgressRunOutcomes).
/// </summary>
public enum EgressRunOutcome
{
    Success,
    Failed,
    Draft,
    Abandoned
}
