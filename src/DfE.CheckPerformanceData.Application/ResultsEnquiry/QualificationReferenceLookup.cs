using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The parsed QualList qualification reference document (AB#297848): a QAN-keyed map of
/// <see cref="QualificationReference"/>, mirroring <see cref="GradeReferenceLookup"/>. Lookups are
/// case-insensitive and trimmed because a QAN can end in a letter (<c>6037116X</c>) and arrives
/// from a supplier export whose casing and padding are not guaranteed.
/// </summary>
public sealed class QualificationReferenceLookup
{
    private readonly Dictionary<string, QualificationReference> _byQan;

    private QualificationReferenceLookup(Dictionary<string, QualificationReference> byQan)
    {
        _byQan = byQan;
        AwardingOrganisations = _byQan.Values
            .Select(q => q.AwardingOrganisation)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static QualificationReferenceLookup Empty { get; } = new([]);

    /// <summary>All entries, keyed by the QAN key from the document.</summary>
    public IReadOnlyDictionary<string, QualificationReference> Entries => _byQan;

    /// <summary>Distinct AO names, sorted for the dropdown. Computed once at parse.</summary>
    public IReadOnlyList<string> AwardingOrganisations { get; }

    /// <summary>That AO's qualifications sorted by title (exact AO match — names come from the
    /// same document the dropdown was rendered from, so a mismatch is a forged post).</summary>
    public IReadOnlyList<QualificationReference> ForAwardingOrganisation(string ao) =>
        _byQan.Values.Where(q => string.Equals(q.AwardingOrganisation, ao, StringComparison.Ordinal))
            .OrderBy(q => q.QualificationTitle, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The qualification for a QAN, or <c>null</c> when it is not in the reference data. A
    /// reference-data gap is a normal (if loggable) state, not an exception.</summary>
    public QualificationReference? Find(string? qan)
        => string.IsNullOrWhiteSpace(qan) ? null : _byQan.GetValueOrDefault(qan.Trim());

    /// <summary>Parses the reference document. Malformed JSON throws so a broken file surfaces
    /// rather than reading as "no qualification exists".</summary>
    public static QualificationReferenceLookup Parse(string json)
    {
        var parsed = JsonSerializer.Deserialize<Dictionary<string, QualificationReference>>(
            json, ResultsEnquiryJson.Options);

        if (parsed is null or { Count: 0 })
            return Empty;

        return new QualificationReferenceLookup(
            new Dictionary<string, QualificationReference>(parsed, StringComparer.OrdinalIgnoreCase));
    }
}
