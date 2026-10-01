using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Dashboard;

namespace DfE.CheckPerformanceData.Application.Journey.NotOnRoll;

/// <summary>
/// The FE colleges that may choose "Not on roll" as a KS4 June removal reason, beside
/// independent schools (AB#304119). Read from <c>not-on-roll-colleges.json</c> in the
/// rules-config container. A college matches on LAESTAB only, reduced to digits, so the
/// sign-in claim "211/8066" and the file's "2118066" are the same school.
/// </summary>
public sealed class NotOnRollCollegeList
{
    /// <summary>The blob name in the rules-config container, and the bundled file name.</summary>
    public const string BlobName = "not-on-roll-colleges.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly HashSet<string> _laestabs;

    private NotOnRollCollegeList(HashSet<string> laestabs) => _laestabs = laestabs;

    public static NotOnRollCollegeList Empty { get; } = new([]);

    public int Count => _laestabs.Count;

    /// <summary>True when the LAESTAB, in any format, is a listed college. Empty never matches.</summary>
    public bool Contains(string? laestab)
    {
        var normalised = LaestabNormaliser.Normalise(laestab);
        return normalised.Length > 0 && _laestabs.Contains(normalised);
    }

    /// <summary>
    /// Parses the document. Malformed JSON throws, so the caller keeps the list it had rather
    /// than reading a broken file as "no college may record not on roll".
    /// </summary>
    public static NotOnRollCollegeList Parse(string json)
    {
        var document = JsonSerializer.Deserialize<Document>(json, JsonOptions);

        var laestabs = (document?.Colleges ?? [])
            .Select(c => LaestabNormaliser.Normalise(c.Laestab))
            .Where(l => l.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return new NotOnRollCollegeList(laestabs);
    }

    private sealed record Document(
        [property: JsonPropertyName("colleges")] IReadOnlyList<College>? Colleges);

    private sealed record College(
        [property: JsonPropertyName("laestab")] string? Laestab);
}
