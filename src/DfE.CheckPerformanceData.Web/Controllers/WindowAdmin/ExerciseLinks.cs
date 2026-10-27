namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>Links back to the edit checking exercise page.</summary>
public static class ExerciseLinks
{
    /// <summary>
    /// The URL fragment that opens the Data tab. The GOV.UK tabs component opens the tab whose
    /// panel id matches the fragment, so this must stay the same as the <c>govuk-tabs-item</c> id
    /// in <c>CreateCheckingExercise.cshtml</c>.
    /// </summary>
    public const string DataTab = "Data";
}
