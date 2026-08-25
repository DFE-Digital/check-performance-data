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
        "MatchRef",
        "Laestab",
        "EntryDate",
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

    // Sensitive leaf keys on a SubmittedBy object. DisplayName is a name; UserId is a stable
    // identifier — both re-identify and must be masked.
    private static readonly HashSet<string> SubmittedBySensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DisplayName",
        "UserId",
    };

    // Sensitive leaf keys on an uploaded-evidence file. Filenames routinely embed a pupil's
    // name (e.g. "Jane Doe birth certificate.pdf"), so both the original and stored names are
    // masked.
    private static readonly HashSet<string> FileSensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "OriginalFileName",
        "StoredFileName",
    };

    private static void MaskSubmittedBy(JsonNode? node)
    {
        if (node is not JsonObject submittedBy)
            return;

        foreach (var key in submittedBy.Select(p => p.Key).ToList())
        {
            if (SubmittedBySensitiveKeys.Contains(key))
                submittedBy[key] = Mask;
        }
    }

    private static void MaskAnswers(JsonNode? node)
    {
        if (node is not JsonArray answers)
            return;

        foreach (var answer in answers)
        {
            if (answer is not JsonObject obj)
                continue;

            if (obj.ContainsKey("Value") && obj["Value"] is not null)
                obj["Value"] = Mask;

            MaskFiles(obj["Files"]);
        }
    }

    private static void MaskFiles(JsonNode? node)
    {
        if (node is not JsonArray files)
            return;

        foreach (var file in files)
        {
            if (file is not JsonObject obj)
                continue;

            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                if (FileSensitiveKeys.Contains(key))
                    obj[key] = Mask;
            }
        }
    }
}
