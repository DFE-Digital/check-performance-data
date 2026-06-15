namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// The deserialized shape of a single pupil in a school's per-window JSON file
/// (<c>data/{laestab}_pupils.json</c> in the <c>{windowId}</c> container). The field set
/// mirrors the former <c>Pupil</c> database entity; the external service supplies the
/// stable <see cref="Id"/>. This is the source for the pupil DTOs that used to be projected
/// from EF Core.
/// </summary>
public sealed class PupilRecord
{
    public Guid Id { get; init; }
    public Guid CheckingWindowId { get; init; }
    public string Laestab { get; init; } = string.Empty;
    public string Surname { get; init; } = string.Empty;
    public string Firstname { get; init; } = string.Empty;
    public string Sex { get; init; } = string.Empty;
    public string DateOfBirth { get; init; } = string.Empty;
    public int Age { get; init; }
    public string FirstLanguage { get; init; } = string.Empty;
    public int Pincl { get; init; }
    public bool NewMobile { get; init; }
    public string ActualYearGroup { get; init; } = string.Empty;
    public string Ethnicity { get; init; } = string.Empty;
    public string SenF { get; init; } = string.Empty;
    public DateTime EntryDate { get; init; }
    public string Urn { get; init; } = string.Empty;
    public string Cypmd_Id { get; init; } = string.Empty;
    public int MatchRef { get; init; }
    public string Upn { get; init; } = string.Empty;
}
