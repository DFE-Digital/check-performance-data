namespace DfE.CheckPerformanceData.Domain.Enums;

/// <summary>
/// The two outcomes the audit log can show for a data-egress transfer (AB#294592). Only egress
/// audit rows carry one; every other audited activity has no outcome.
/// </summary>
public enum AuditOutcome
{
    Success,
    Failed
}
