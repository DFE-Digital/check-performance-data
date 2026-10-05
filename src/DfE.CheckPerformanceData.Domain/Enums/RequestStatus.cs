namespace DfE.CheckPerformanceData.Domain.Enums;

// What the school did with the request. Only the web app writes it (and the close sweep's draft
// step, InProgress / ReadyToSubmit -> NotSubmitted). Where a submitted request is on its way to
// Zendesk is ChangeRequest.ProcessingStatus, never this column (#536).
public enum RequestStatus
{
    InProgress,
    ReadyToSubmit,
    Submitted,
    Withdrawn,

    // A draft (InProgress / ReadyToSubmit) that was never submitted before its checking exercise
    // was closed.
    NotSubmitted
}

// Where a submitted request is on its way to a Zendesk ticket. Each step has its own writer:
//   null           - submitted; the Rules Engine has not decided it yet
//   Decided        - RulesConsumer stored a decision (amendments only); waiting for the close sweep
//   TicketQueued   - on the zendesk queue: put there by the close sweep (amendments) or at submit
//                    (results enquiries, which never pass the Rules Engine)
//   TicketCreating - ZendeskConsumer has claimed it and is calling Zendesk
//   TicketCreated  - ZendeskConsumer stored the ticket id in CrmId
public enum ProcessingStatus
{
    Decided,
    TicketQueued,
    TicketCreating,
    TicketCreated
}
