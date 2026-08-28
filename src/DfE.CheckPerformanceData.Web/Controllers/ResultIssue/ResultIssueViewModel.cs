namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class ResultIssueViewModel
{
    /// <summary>AB#296648: the "report an incorrect grade" enquiry. "Result does not belong to
    /// pupil" remains a sibling ticket with no journey, so posting it is still rejected as
    /// unanswered.</summary>
    public const string IncorrectGrade = "incorrect-grade";

    /// <summary>AB#297848: the missing-qualification enquiry. "Result does not belong to pupil"
    /// remains a sibling ticket with no journey, so posting it is still rejected as unanswered.</summary>
    public const string MissingQualification = "missing-qualification";

    public Guid WindowId { get; set; }

    /// <summary>The posted radio value. Null on first render and on a validation redisplay.</summary>
    public string? IssueType { get; set; }
}
