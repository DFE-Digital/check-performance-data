using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

public class OpenCheckingWindowDto
{
    public required string Title { get; init; }
    public required DateOnly EndDate { get; init; }
    public required KeyStages KeyStage { get; init; }
}