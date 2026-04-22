namespace DfE.CheckPerformanceData.Persistence.Entities;

public class Pupil
{
    public Guid Id { get; init; }
    public Guid CheckingWindowId { get; set; }
    public string Laestab { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Firstname { get; set; } = string.Empty;
    public string Sex { get; set; } = string.Empty;
    public string DateOfBirth { get; set; } = string.Empty;
    public int Age { get; set; }
    public string FirstLanguage { get; set; } = string.Empty;
    public int Pincl { get; set; }
}
