using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The decision a pulled record carries: one of the Zendesk "Decision status" option values, or one
/// of the two CYPMD-side markers for a request that has no readable ticket. The filter step keeps
/// approved and auto_approved only (AB#292610).
/// </summary>
public static class EgressDecisions
{
    /// <summary>The request row has no CrmId — it was never sent to Zendesk.</summary>
    public const string NoTicket = "no-ticket";

    /// <summary>Zendesk returned nothing for the stored ticket id.</summary>
    public const string NotFound = "not-found";

    public static bool IsApproved(string? decision) =>
        string.Equals(decision, ZendeskTicketFieldOptions.DecisionStatus.Approved, StringComparison.OrdinalIgnoreCase)
        || string.Equals(decision, ZendeskTicketFieldOptions.DecisionStatus.AutoApproved, StringComparison.OrdinalIgnoreCase);

    public static string Label(string? decision) => decision?.ToLowerInvariant() switch
    {
        "approved" => "Approved",
        "auto_approved" => "Auto approved",
        "rejected" => "Rejected",
        "auto_rejected" => "Auto rejected",
        "scrutiny" => "Scrutiny",
        NoTicket => "No Zendesk ticket",
        NotFound => "Ticket not found",
        null or "" => "Unknown",
        var other => other
    };
}
