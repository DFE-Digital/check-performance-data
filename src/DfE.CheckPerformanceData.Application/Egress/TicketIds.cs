namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The parse every reader of the stored Zendesk ticket id (CrmId) shares: egress and the decision
/// pull must treat a missing or unparseable id the same way. A bad value is null, never an
/// exception — the caller decides what a bad id means.
/// </summary>
public static class TicketIds
{
    public static long? Parse(string? crmId) =>
        long.TryParse(crmId, out var id) && id > 0 ? id : null;
}
