using DfE.CheckPerformanceData.Domain.Entities;

namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

public class OpenWindowDto
{
    public string Title { get; set; }
    public DateTime EndDate { get; set; }
    public KeyStages KeyStage { get; set; }
}