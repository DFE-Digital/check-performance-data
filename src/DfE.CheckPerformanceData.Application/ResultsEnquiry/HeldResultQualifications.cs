using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>Finds the qualification reference entry of a result that a learner holds.</summary>
public static class HeldResultQualifications
{
    /// <summary>
    /// Searches the documents of <see cref="QualificationReferenceSets.ForHeldResult"/> in order
    /// and gives the first entry for the QAN.
    /// </summary>
    /// <returns>The entry, or null when the QAN is blank or no document holds it.</returns>
    public static async Task<QualificationReference?> FindHeldResultAsync(
        this IQualificationReferenceClient client, CheckingWindowType? windowType, string? qan,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(qan)) return null;

        foreach (var set in QualificationReferenceSets.ForHeldResult(windowType))
        {
            var lookup = await client.GetLookupAsync(set, ct);
            if (lookup.Find(qan) is { } qualification) return qualification;
        }

        return null;
    }
}
