using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// The deserialized shape of a single pupil in a school's per-window JSON file
/// (<c>data/{laestab}_pupils.json</c> in the <c>{windowId}</c> container), matching the
/// confirmed supplier schema. The supplier uses UPPER_SNAKE field names that do not map to
/// our C# names by a uniform rule, so each property carries an explicit
/// <see cref="JsonPropertyNameAttribute"/>.
///
/// The class-level <see cref="JsonNumberHandlingAttribute"/> makes numeric properties bind
/// whether the supplier sends a JSON number or a quoted string (e.g. <c>401</c> or
/// <c>"401"</c>). <c>ENTRYDAT</c> and <c>DOB</c> are kept as raw strings because their date
/// format is supplier-defined; <c>NEWMOBILE</c> is bound via <see cref="NumericBoolJsonConverter"/>
/// (it arrives as <c>0</c>/<c>1</c>). The supplier's <c>P_INCL_DESC</c> is intentionally not
/// captured — unknown JSON fields are ignored.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class PupilRecord : IPupilRecord
{
    [JsonPropertyName("Id")]
    public Guid Id { get; init; }

    [JsonPropertyName("CheckingWindowId")]
    public Guid CheckingWindowId { get; init; }

    [JsonPropertyName("UPN")]
    public string Upn { get; init; } = string.Empty;

    [JsonPropertyName("MATCHREF")]
    public int MatchRef { get; init; }

    [JsonPropertyName("CYPMD_ID")]
    public string Cypmd_Id { get; init; } = string.Empty;

    [JsonPropertyName("SURNAME")]
    public string Surname { get; init; } = string.Empty;

    [JsonPropertyName("FORENAME")]
    public string Firstname { get; init; } = string.Empty;

    [JsonPropertyName("SEX")]
    public string Sex { get; init; } = string.Empty;

    [JsonPropertyName("DOB")]
    public string DateOfBirth { get; init; } = string.Empty;

    [JsonPropertyName("AGE")]
    public int Age { get; init; }

    // Nullable per the supplier schema (P_INCL is ["string","null"]); null / absent means the
    // inclusion flag was not supplied. Consumers treat null as "not included".
    [JsonPropertyName("P_INCL")]
    public int? Pincl { get; init; }

    [JsonPropertyName("LAESTAB")]
    public string Laestab { get; init; } = string.Empty;

    [JsonPropertyName("URN")]
    public long Urn { get; init; }

    [JsonPropertyName("ENTRYDAT")]
    public string EntryDate { get; init; } = string.Empty;

    [JsonPropertyName("SENF")]
    public string SenF { get; init; } = string.Empty;

    [JsonPropertyName("LANG1ST")]
    public string FirstLanguage { get; init; } = string.Empty;

    [JsonPropertyName("ETHNIC")]
    public string Ethnicity { get; init; } = string.Empty;

    [JsonPropertyName("ACTYRGRP")]
    public string ActualYearGroup { get; init; } = string.Empty;

    [JsonPropertyName("NEWMOBILE")]
    [JsonConverter(typeof(NumericBoolJsonConverter))]
    public bool NewMobile { get; init; }

    [JsonIgnore]
    public string Identifier => Upn;

    [JsonIgnore]
    public bool IsIncluded => PupilInclusion.IsKs4Included(Pincl);
}
