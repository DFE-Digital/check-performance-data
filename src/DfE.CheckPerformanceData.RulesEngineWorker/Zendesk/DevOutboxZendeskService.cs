using System.Text.Json;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Zendesk;

// Development-only stand-in for the real Zendesk service. Instead of calling Zendesk it
// records the "created" ticket into the shared dev outbox table so the pipeline can be
// observed end-to-end locally, then returns a synthetic response carrying a fake ticket id
// so the consumer's CrmId write and idempotency check still complete. Registered only
// outside Production (see worker startup); the real service is used everywhere else.
// Every method other than CreateTicketAsync is unsupported here — the dev flow only
// exercises ticket creation.
public sealed partial class DevOutboxZendeskService(IPortalDbContext dbContext) : IZendeskService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    // Synthetic ticket ids: a monotonic source unique enough for local observation.
    private static long _nextTicketId = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public async Task<CreateTicketResponseDto> CreateTicketAsync(CreateTicketRequestDto request)
    {
        var ticketId = Interlocked.Increment(ref _nextTicketId);
        var ticket = request.Ticket;

        dbContext.DevZendeskTickets.Add(new DevZendeskTicket
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            ReferenceNumber = ExtractReference(ticket?.Subject),
            Subject = ticket?.Subject ?? string.Empty,
            Priority = ticket?.Priority ?? string.Empty,
            Status = ticket?.Status ?? string.Empty,
            TicketId = ticketId,
            RawJson = JsonSerializer.Serialize(request, SerializerOptions),
        });

        await dbContext.SaveChangesAsync();

        return new CreateTicketResponseDto
        {
            Ticket = new TicketDto { Id = ticketId },
        };
    }

    // The reference number is embedded as a trailing "(REF-...)" by the ticket builder.
    // Best-effort only: a row is still captured (with an empty reference) if it is absent.
    private static string ExtractReference(string? subject)
    {
        if (string.IsNullOrEmpty(subject))
            return string.Empty;

        var match = ReferenceInSubject().Match(subject);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [GeneratedRegex(@"\(([^()]+)\)\s*$")]
    private static partial Regex ReferenceInSubject();

    public Task<ListViewsResponseDto> ListViewsAsync(int? pageSize) =>
        throw new NotSupportedException("DevOutboxZendeskService only supports CreateTicketAsync.");

    public Task<ListViewTicketsResponseDto> GetTicketsForView(long viewId, Dictionary<string, object>? query = null) =>
        throw new NotSupportedException("DevOutboxZendeskService only supports CreateTicketAsync.");

    public Task<TicketFieldsResponseDto> GetTicketFields() =>
        throw new NotSupportedException("DevOutboxZendeskService only supports CreateTicketAsync.");

    public Task<UserFieldsResponseDto> GetUserFieldsAsync() =>
        throw new NotSupportedException("DevOutboxZendeskService only supports CreateTicketAsync.");

    public Task<TicketsViewModel> GetTicketsViewModelAsync(long viewId, Dictionary<string, object>? query = null) =>
        throw new NotSupportedException("DevOutboxZendeskService only supports CreateTicketAsync.");

    public Task<GetTicketResponseDto> GetTicketAsync(long ticketId) =>
        throw new NotSupportedException("DevOutboxZendeskService only supports CreateTicketAsync.");

    public Task<TicketCommentsResponseDto> GetTicketComments(long ticketId) =>
        throw new NotSupportedException("DevOutboxZendeskService only supports CreateTicketAsync.");

    public Task<GetTicketViewModel> GetTicketViewModelAsync(long ticketId) =>
        throw new NotSupportedException("DevOutboxZendeskService only supports CreateTicketAsync.");
}
