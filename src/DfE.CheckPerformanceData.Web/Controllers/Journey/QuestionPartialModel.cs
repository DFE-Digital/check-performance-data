using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

public sealed class QuestionPartialModel
{
    public Guid WindowId { get; init; }
    public required string PageId { get; init; }
    public required Question Question { get; init; }
    public QuestionAnswer? ExistingAnswer { get; init; }
    public bool FromSummary { get; init; }
    public bool IsPageHeading { get; init; }
    public string? Error { get; init; }

    public string FieldName => $"q_{Question.Id.Replace("-", "_")}";
    public bool HasError => Error is not null;
    public string ResolvedTitle { get; init; } = string.Empty;
}
