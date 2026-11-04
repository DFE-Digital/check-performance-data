namespace DfE.CheckPerformanceData.Domain.Enums;

// The decision Zendesk holds for a submitted request, pulled back by the admin per-exercise
// decision pull (#746). Set only to a final value: a ticket still under review stores nothing.
// Distinct from the rules-engine DecisionStatus (Application), which is what the rules engine
// decided before the ticket was created.
public enum ZendeskDecision
{
    Accepted,
    Rejected
}
