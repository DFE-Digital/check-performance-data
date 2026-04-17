using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

public class OpenCheckingWindowDto
{
    public string Title { get; set; }
    public DateTime EndDate { get; set; }
    public KeyStages KeyStage { get; set; }
}