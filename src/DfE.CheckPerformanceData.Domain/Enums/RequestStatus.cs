namespace DfE.CheckPerformanceData.Domain.Enums;

public enum RequestStatus
{
    InProgress,
    ReadyToSubmit,
    SubmittedUnCommitted,
    SubmittedWithdrawn,
    RulesProcessed,
    ZendeskTicketCreating,
    ZendeskTicketCreated
}
