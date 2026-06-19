using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.Queue;

// Pulls the request reference out of a queued RequestDocument payload so the queue admin
// surfaces can link a message to its journey timeline. Best-effort by design: any payload
// that cannot be parsed, or that carries no non-empty string ReferenceNumber, yields null
// (no link) rather than an exception — the admin pages must render whatever shape the
// payload happens to be. The reference is the redaction-safe key the observability surfaces
// already display; no pupil field is read here.
public static class QueuePayloadReference
{
    public static string? TryExtract(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty("ReferenceNumber", out var reference) ||
                reference.ValueKind != JsonValueKind.String)
                return null;

            var value = reference.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
