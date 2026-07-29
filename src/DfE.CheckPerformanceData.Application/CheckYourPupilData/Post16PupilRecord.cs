using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// The 16-19 (Post16) pupil read model. LDS supplies 16-19 pupils as *two* CSVs — an included
/// file (~80 columns) and a non-included file (12 columns) — which ingress merges into one
/// <c>data/{laestab}_pupils.json</c> per school. This record is therefore the union of both
/// shapes: everything the non-included file lacks is nullable or defaulted.
///
/// The non-included file has no <c>P_INCL</c> column at all, so inclusion is NOT derived from a
/// code here. Ingress stamps an <c>INCLUDED</c> boolean from the file of origin and
/// <see cref="IsIncluded"/> reads that.
///
/// Field names differ from KS4: <c>FORENAMES</c> (plural), and identity is <c>ULN</c> — 16-19
/// has no UPN. <c>URN</c>/<c>UKPRN</c> are strings here (the supplier declares them varchar and
/// they are display-only) unlike the KS4 <c>long Urn</c>.
///
/// Only the fields the pupils table, CSV and journey read are bound. The ~68 qualification,
/// progress and retention fields are deliberately not declared yet — unknown JSON fields are
/// ignored, so they can be added when the 16-19 column set is confirmed.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class Post16PupilRecord : IPupilRecord
{
    [JsonPropertyName("Id")]
    public Guid Id { get; init; }

    [JsonPropertyName("CheckingWindowId")]
    public Guid CheckingWindowId { get; init; }

    /// <summary>Stamped by ingress from the file of origin — not a supplier column.</summary>
    [JsonPropertyName("INCLUDED")]
    public bool Included { get; init; }

    [JsonPropertyName("CYPMD_ID")]
    public string Cypmd_Id { get; init; } = string.Empty;

    [JsonPropertyName("SURNAME")]
    public string Surname { get; init; } = string.Empty;

    [JsonPropertyName("FORENAMES")]
    public string Firstname { get; init; } = string.Empty;

    [JsonPropertyName("SEX")]
    public string Sex { get; init; } = string.Empty;

    /// <summary>Raw supplier string (schema says <c>YYYY-MM-DD HH:MM:SS.SSS</c>); formatted for
    /// display by <see cref="PupilDateFormatter"/>.</summary>
    [JsonPropertyName("DOB")]
    public string DateOfBirth { get; init; } = string.Empty;

    [JsonPropertyName("AGE")]
    public int Age { get; init; }

    /// <summary>Included file only: 501/502/505/506 or NULL. Absent from the non-included file.
    /// Not used for the inclusion split — see <see cref="IsIncluded"/>.</summary>
    [JsonPropertyName("P_INCL")]
    public int? Pincl { get; init; }

    /// <summary>Included file only: 503/504.</summary>
    [JsonPropertyName("P_INCL_aims")]
    public int? PinclAims { get; init; }

    [JsonPropertyName("LAESTAB")]
    public string Laestab { get; init; } = string.Empty;

    [JsonPropertyName("URN")]
    public string Urn { get; init; } = string.Empty;

    [JsonPropertyName("UKPRN")]
    public string Ukprn { get; init; } = string.Empty;

    [JsonPropertyName("ULN")]
    public string Uln { get; init; } = string.Empty;

    /// <summary>Non-included file only. Meaning unconfirmed with LDS.</summary>
    [JsonPropertyName("CampID_0")]
    public string CampId0 { get; init; } = string.Empty;

    /// <summary>Non-included file only. Meaning unconfirmed with LDS.</summary>
    [JsonPropertyName("CampID_1")]
    public string CampId1 { get; init; } = string.Empty;

    [JsonIgnore]
    public string Identifier => Uln;

    [JsonIgnore]
    public bool IsIncluded => Included;
}
