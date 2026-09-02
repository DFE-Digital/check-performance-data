namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class ResultIssueViewModel
{
    /// <summary>AB#296648: the "report an incorrect grade" enquiry.</summary>
    public const string IncorrectGrade = "incorrect-grade";

    /// <summary>AB#297848: the missing-qualification enquiry.</summary>
    public const string MissingQualification = "missing-qualification";

    /// <summary>AB#298704: the "result does not belong to student" enquiry.</summary>
    public const string ResultDoesNotBelong = "result-does-not-belong";

    public Guid WindowId { get; set; }

    /// <summary>The posted radio value. Null on first render and on a validation redisplay.</summary>
    public string? IssueType { get; set; }
}
