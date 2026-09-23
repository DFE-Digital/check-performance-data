using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.Audit;

/// <summary>
/// The fields the audit log reads back out of an EgressRun audit row's NewValues (AB#294592).
/// The transfer writes camelCase JSON (JsonSerializerDefaults.Web); the success payload has always
/// carried windowId/outputTypes/transferredBy, the failure payload only since this ticket, so every
/// member is optional and an unreadable payload is null — the row still lists, just undecorated.
/// </summary>
public sealed record EgressAuditPayload(string? Outcome, Guid? WindowId, IReadOnlyList<string>? OutputTypes, string? TransferredBy)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static EgressAuditPayload? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<EgressAuditPayload>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
