using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The parsed AODC grade reference document: a QAN-keyed map of <see cref="GradeReference"/>.
///
/// Parsing lives here rather than in the blob client so the shipped seed file can be validated by
/// unit tests without blob storage, and so the client is left with only the fetch-and-cache job.
/// Lookups are case-insensitive and trimmed because a QAN can end in a letter (<c>6037116X</c>) and
/// arrives from a supplier CSV whose casing and padding are not guaranteed.
/// </summary>
public sealed class GradeReferenceLookup
{
    private readonly Dictionary<string, GradeReference> _byQan;

    private GradeReferenceLookup(Dictionary<string, GradeReference> byQan) => _byQan = byQan;

    public static GradeReferenceLookup Empty { get; } = new([]);

    /// <summary>All entries, keyed by the QAN key from the document.</summary>
    public IReadOnlyDictionary<string, GradeReference> Entries => _byQan;

    /// <summary>The grades for a QAN, or <c>null</c> when the qualification is not in the reference
    /// data. A reference-data gap is a normal (if loggable) state, not an exception.</summary>
    public GradeReference? Find(string? qan)
        => string.IsNullOrWhiteSpace(qan) ? null : _byQan.GetValueOrDefault(qan.Trim());

    /// <summary>Parses the reference document. Malformed JSON throws so a broken file surfaces
    /// rather than reading as "no qualification has any grades".</summary>
    public static GradeReferenceLookup Parse(string json)
    {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, GradeReference>>(
            json, ResultsEnquiryJson.Options);

        if (parsed is null or { Count: 0 })
            return Empty;

        return new GradeReferenceLookup(
            new Dictionary<string, GradeReference>(parsed, StringComparer.OrdinalIgnoreCase));
    }
}
