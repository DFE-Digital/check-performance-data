namespace DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;

public sealed class PupilRow
{
    public required string Surname { get; init; }
    public required string Firstname { get; init; }
    public required string Sex { get; init; }
    public required string DateOfBirth { get; init; }
    public required int Age { get; init; }
    public required string FirstLanguage { get; init; }
}