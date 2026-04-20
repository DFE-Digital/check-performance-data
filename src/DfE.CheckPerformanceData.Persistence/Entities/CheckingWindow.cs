using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Persistence.Entities;

public sealed class CheckingWindow
{
    public Guid Id { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public KeyStages KeyStage { get; init; }
    public string Title { get; init; } = string.Empty;
}