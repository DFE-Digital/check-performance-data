using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The 16-19 per-student result read model (AB#296999). One record per result row across the six
/// input CSVs; <see cref="SourceFile"/> carries the provenance tag so the school can trace which
/// file a result came from. Unknown JSON fields are ignored so ingestion can add columns without
/// breaking readers.
/// </summary>
public sealed class StudentResultRecord
{
    [JsonPropertyName("CYPMD_ID")] public string CypmdId { get; init; } = string.Empty;
    [JsonPropertyName("QAN")] public string Qan { get; init; } = string.Empty;

    /// <summary>e.g. "GCSE (9-1) Bus. Studs:Single".</summary>
    [JsonPropertyName("QUAL_NAME")] public string QualificationName { get; init; } = string.Empty;

    [JsonPropertyName("SYLLABUS")] public string SyllabusCode { get; init; } = string.Empty;

    /// <summary>e.g. "S2024".</summary>
    [JsonPropertyName("SESSION")] public string Session { get; init; } = string.Empty;

    [JsonPropertyName("GRADE")] public string Grade { get; init; } = string.Empty;

    /// <summary>A <see cref="ResultsFileTags"/> value.</summary>
    [JsonPropertyName("SOURCE")] public string SourceFile { get; init; } = string.Empty;

    /// <summary>
    /// The composite key that uniquely identifies this result for the pupil, used as the posted
    /// value of a ResultSearch selection. A pupil can hold the same QAN across sessions and across
    /// source files, so QAN alone is not enough.
    /// </summary>
    [JsonIgnore]
    public string CompositeKey => $"{Qan}|{Session}|{SourceFile}";
}
