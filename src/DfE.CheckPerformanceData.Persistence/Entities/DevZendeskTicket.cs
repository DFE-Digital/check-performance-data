namespace DfE.CheckPerformanceData.Persistence.Entities;

// A captured "Zendesk ticket" recorded by the development-only fake Zendesk service so the
// rules-engine pipeline can be observed end-to-end locally with no real Zendesk. The worker
// process writes rows here; the web process reads them for the dev outbox viewer, so the
// capture must live in the shared database rather than an in-memory worker outbox. Has no
// production counterpart — the fake that writes it is only registered outside Production.
public sealed class DevZendeskTicket
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long TicketId { get; set; }
    public string RawJson { get; set; } = string.Empty;
}
