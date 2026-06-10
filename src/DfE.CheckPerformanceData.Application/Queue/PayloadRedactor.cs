using System.Text.Json;
using System.Text.Json.Nodes;

namespace DfE.CheckPerformanceData.Application.Queue;

// Masks pupil identifiers in a queued request payload so operators can inspect the
// shape of a message without exposing personal data. Structure and keys are preserved;
// only the sensitive leaf values are replaced with a fixed placeholder.
public sealed class PayloadRedactor
{
    public const string Mask = "[REDACTED]";

    // Sensitive leaf keys carried directly under the Pupil / MatchedPupil objects.
    private static readonly HashSet<string> PupilSensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Upn",
        "Firstname",
        "Surname",
        "DateOfBirth",
        "Id",
        "CypmdId",
        "Sex",
        "Age",
    };

    // Object property names whose entire subtree of sensitive keys must be masked.
    private static readonly HashSet<string> PupilContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pupil",
        "MatchedPupil",
    };

    public string Redact(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Mask;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            // Never surface a payload we cannot safely walk.
            return Mask;
        }

        if (root is not JsonObject obj)
            return Mask;

        MaskPupilContainer(obj["Pupil"]);
        MaskPupilContainer(obj["MatchedPupil"]);
        MaskSubmittedBy(obj["SubmittedBy"]);
        MaskAnswers(obj["Answers"]);

        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void MaskPupilContainer(JsonNode? node)
    {
        if (node is not JsonObject pupil)
            return;

        foreach (var key in pupil.Select(p => p.Key).ToList())
        {
            if (PupilSensitiveKeys.Contains(key))
                pupil[key] = Mask;
        }
    }

    private static void MaskSubmittedBy(JsonNode? node)
    {
        if (node is JsonObject submittedBy && submittedBy.ContainsKey("DisplayName"))
            submittedBy["DisplayName"] = Mask;
    }

    private static void MaskAnswers(JsonNode? node)
    {
        if (node is not JsonArray answers)
            return;

        foreach (var answer in answers)
        {
            if (answer is JsonObject obj && obj.ContainsKey("Value") && obj["Value"] is not null)
                obj["Value"] = Mask;
        }
    }

    // Container-name aware helper kept for callers that hold the field-set; the public
    // Redact entrypoint already targets the known container/leaf keys.
    public static bool IsPupilContainer(string propertyName) => PupilContainers.Contains(propertyName);
}
