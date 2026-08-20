namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class ResultIssueViewModel
{
    /// <summary>
    /// The only issue type this ticket implements. Sibling tickets add "Missing qualification" and
    /// "Result does not belong to pupil" — both appear on the Figma screen but neither has a journey
    /// yet, so posting them is rejected as unanswered rather than starting a flow that does not exist.
    /// </summary>
    public const string IncorrectGrade = "incorrect-grade";

    public Guid WindowId { get; set; }

    /// <summary>The posted radio value. Null on first render and on a validation redisplay.</summary>
    public string? IssueType { get; set; }
}
