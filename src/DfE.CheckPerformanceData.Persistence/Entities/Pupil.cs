namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// As of now, this is a KS4 June pupil.  Other Checking windows pupil records will have more or less fields than this.
/// To get KS4 June up and running I'm just using this one class, but once we get different Checking Windows, we
/// should find the common fields between the windows for pupils and then any window specific fields can be in a separate model.
/// This is the plan at the moment to avoid us having separate screens and logic for each window.
/// </summary>
public sealed class Pupil
{
    public required Guid Id { get; init; }
    public required Guid CheckingWindowId { get; init; }
    public required string Laestab { get; init; } = string.Empty;
    public required string Surname { get; init; } = string.Empty;
    public required string Firstname { get; init; } = string.Empty;
    public required string Sex { get; init; } = string.Empty;
    public required string DateOfBirth { get; init; } = string.Empty;
    public required int Age { get; init; }
    public required string FirstLanguage { get; init; } = string.Empty;
    public required int Pincl { get; init; }
    
    public required bool NewMobile { get; init; }
    public required string ActualYearGroup { get; init; }
    public required string Ethnicity { get; init; }
    public required string SenF { get; init; }
    public required DateTime EntryDate { get; init; }
    public required string Urn { get; init; }
    public required string Cypmd_Id { get; init; }
    public required int MatchRef { get; init; }
    public required string Upn { get; init; }
}