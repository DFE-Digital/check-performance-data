namespace DfE.CheckPerformanceData.Web.QuestionFlow;

public sealed class QuestionFlowConfig
{
    public required string FirstQuestionId { get; init; }
    public required List<Question> Questions { get; init; }
}
