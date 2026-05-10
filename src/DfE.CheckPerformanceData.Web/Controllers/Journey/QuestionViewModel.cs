using DfE.CheckPerformanceData.Web.QuestionFlow;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class QuestionViewModel
{
    public Guid WindowId { get; init; }
    public required Question Question { get; init; }
    public QuestionAnswer? ExistingAnswer { get; init; }
    public string? BackQuestionId { get; init; }
    public bool FromSummary { get; init; }
}
