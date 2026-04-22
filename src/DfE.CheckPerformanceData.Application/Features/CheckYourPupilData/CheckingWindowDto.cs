namespace DfE.CheckPerformanceData.Application.Features.CheckYourPupilData;

public class CheckingWindowDto
{
    public required DateOnly EndDate { get; init; }
    public required string Title { get; init; }
}