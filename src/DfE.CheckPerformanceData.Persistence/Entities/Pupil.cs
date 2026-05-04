namespace DfE.CheckPerformanceData.Persistence.Entities;

public class Pupil
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
}