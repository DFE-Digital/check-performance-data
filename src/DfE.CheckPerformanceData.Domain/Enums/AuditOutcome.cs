namespace DfE.CheckPerformanceData.Domain.Enums;

/// <summary>
/// The outcomes the audit log can show (AB#294592). Only a data-egress transfer (either outcome)
/// and an early closure of a checking exercise (AB#301022, always Success) carry one; every other
/// audited activity has no outcome.
/// </summary>
public enum AuditOutcome
{
    Success,
    Failed
}
