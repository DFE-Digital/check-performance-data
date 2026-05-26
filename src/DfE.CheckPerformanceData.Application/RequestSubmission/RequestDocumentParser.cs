using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.RequestSubmission;

/// <summary>
/// Parses a queue-message body into a <see cref="RequestDocument"/> — the same
/// contract the producer serialises when it enqueues a request. Returns
/// <c>null</c> when the body is not valid JSON or is missing a required field;
/// the worker treats <c>null</c> as an unparseable (poison) message.
///
/// The rules engine is the sole decision-maker, so there is deliberately no
/// wire decision/status to honour here — only the request data is read.
/// </summary>
public static class RequestDocumentParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static RequestDocument? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            return JsonSerializer.Deserialize<RequestDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
