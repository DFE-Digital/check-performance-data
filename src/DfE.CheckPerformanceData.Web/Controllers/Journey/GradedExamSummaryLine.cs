namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>The "Graded Exam" check-answers row of a KS4 Autumn results enquiry (#899).</summary>
internal static class GradedExamSummaryLine
{
    private const string Key = "Graded Exam";
    private const string Yes = "Yes";
    private const string No = "No";

    public static SummaryLine For(bool isGradedExam) => new(Key, isGradedExam ? Yes : No, null, false, null);
}
