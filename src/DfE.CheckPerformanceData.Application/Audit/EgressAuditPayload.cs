using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.Audit;

/// <summary>
/// The fields the audit log reads back out of an EgressRun audit row's NewValues (AB#294592).
/// The transfer writes camelCase JSON (JsonSerializerDefaults.Web); the success payload has always
/// carried windowId/outputTypes/transferredBy, the failure payload only since this ticket, so every
/// member is optional and an unreadable payload is null — the row still lists, just undecorated.
/// The generic capture's record of the pull (EgressRun/Insert) is the run row itself in PascalCase;
/// the Web options are case-insensitive, so WindowId and StartedByName bind from it too. The
/// outcome is not read from here: it comes from the row's Action.
/// </summary>
public sealed record EgressAuditPayload(Guid? WindowId, IReadOnlyList<string>? OutputTypes, string? TransferredBy, string? StartedByName)
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
