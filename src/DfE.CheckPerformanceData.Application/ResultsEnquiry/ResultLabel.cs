namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// How one exam result is described to a school when they pick which one is wrong.
///
/// One definition, because the text appears in two places — the server-rendered options on the
/// ResultSearch page and the <c>/results/suggestions</c> endpoint — and they must agree or the
/// enhanced and unenhanced versions of the same page would read differently.
///
/// The Figma frame shows "{qualification}, QAN: {qan}". The session is appended because
/// AB#296648 requires that "each result shows enough detail for me to identify the right one", and
/// its stated rationale is that a pupil can hold several results in one subject area — a resit would
/// otherwise be indistinguishable from the original sitting. FLAGGED for content sign-off.
/// </summary>
public static class ResultLabel
{
    public static string For(StudentResultRecord result)
        => $"{result.QualificationName}, QAN: {result.Qan}, Session: {result.Session}";
}
