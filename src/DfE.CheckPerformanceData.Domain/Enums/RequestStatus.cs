namespace DfE.CheckPerformanceData.Domain.Enums;

public enum RequestStatus
{
    InProgress,
    ReadyToSubmit,
    SubmittedUnCommitted,
    Withdrawn,

    // A draft (InProgress / ReadyToSubmit) that was never submitted before the window's
    // requests were committed to Zendesk.
    NotSubmitted,

    // A SubmittedUnCommitted request that has been committed (dropped onto the Zendesk queue).
    SubmittedCommitted
}

public enum WorkerStatus
{
    RulesProcessed,
    ZendeskTicketCreating,
    ZendeskTicketCreated
}