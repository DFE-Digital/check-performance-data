namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// As of now, this is a KS4 June pupil.  Other Checking windows pupil records will have more or less fields than this.
/// To get KS4 June up and running I'm just using this one class, but once we get different Checking Windows, we
/// should find the common fields between the windows for pupils and then any window specific fields can be in a separate model.
/// This is the plan at the moment to avoid us having separate screens and logic for each window.
/// </summary>
public sealed class Pupil
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
    
    public bool NewMobile { get; set; }
    public string ActualYearGroup { get; set; }
    public string Ethnicity { get; set; }
    public string SenF { get; set; }
    public DateTime EntryDate { get; set; }
    public string Urn { get; set; }
    public string Cypmd_Id { get; set; }
    public int MatchRef { get; set; }
    public string Upn { get; set; }
}