using System.Text.Json;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Entities;

namespace DfE.CheckPerformanceData.Web.Models.Dev;

// A captured outbox ticket reshaped for the Zendesk-styled preview. The stored row carries the
// summary fields plus the full request as RawJson; this parses that JSON into the fields a
// Zendesk ticket page shows (requester, body, custom fields, tags) so the view renders a faithful
// simulation rather than raw JSON. Parsing is best-effort: a malformed or partial row still
// renders with whatever summary fields the row itself carries.
public sealed class ZendeskTicketPreviewViewModel
{
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public long TicketId { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public string Requester { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public IReadOnlyList<ZendeskCustomFieldView> CustomFields { get; init; } = Array.Empty<ZendeskCustomFieldView>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Attachments { get; init; } = Array.Empty<string>();
    public string RawJson { get; init; } = string.Empty;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ZendeskTicketPreviewViewModel FromTicket(DevZendeskTicket ticket)
    {
        CreateTicketDto? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<CreateTicketRequestDto>(ticket.RawJson, Options)?.Ticket;
        }
        catch (JsonException)
        {
            // A malformed row still renders from the summary columns below.
        }

        var customFields = (parsed?.CustomFields ?? new List<CustomFieldDto>())
            .Where(f => f.Value is not null)
            .Select(f => new ZendeskCustomFieldView(
                f.Id?.ToString() ?? string.Empty,
                f.Value?.ToString() ?? string.Empty))
            .ToList();

        var attachments = (parsed?.Comment?.Attachments ?? new List<AttachmentDto>())
            .Select(a => a.FileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return new ZendeskTicketPreviewViewModel
        {
            ReferenceNumber = ticket.ReferenceNumber,
            Subject = string.IsNullOrEmpty(parsed?.Subject) ? ticket.Subject : parsed!.Subject,
            Priority = string.IsNullOrEmpty(parsed?.Priority) ? ticket.Priority : parsed!.Priority,
            Status = string.IsNullOrEmpty(parsed?.Status) ? ticket.Status : parsed!.Status,
            Type = parsed?.Type ?? string.Empty,
            TicketId = ticket.TicketId,
            CreatedAtUtc = ticket.CreatedAtUtc,
            Requester = parsed?.RequesterId is { } rid ? $"Requester #{rid}" : "Unknown requester",
            Body = parsed?.Comment?.Body ?? parsed?.Description ?? string.Empty,
            CustomFields = customFields,
            Tags = Array.Empty<string>(),
            Attachments = attachments,
            RawJson = ticket.RawJson,
        };
    }
}

public sealed record ZendeskCustomFieldView(string Id, string Value);
