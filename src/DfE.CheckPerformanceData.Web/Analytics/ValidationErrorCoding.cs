using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Web.Analytics;

/// <summary>
/// Maps journey validation failures to a controlled, machine-readable code taxonomy
/// for the <c>validation_error</c> analytics event. Codes are derived from the failure
/// kind and question type — never from the raw (PII-bearing) validation messages.
/// </summary>
public static class ValidationErrorCoding
{
    public const string NoSelection = "no_selection";
    public const string SamePupil = "same_pupil";
    public const string AtLeastOne = "at_least_one";
    public const string FileRequired = "file_required";
    public const string Conflict = "conflict";

    /// <summary>A date that is well-formed but inconsistent with another date on the same page,
    /// or with today — distinct from <c>bad_date</c>, which means unparseable.</summary>
    public const string DateInconsistent = "date_inconsistent";

    /// <summary>Code for a question that failed validation. An unanswered required
    /// question is <c>required</c>; an answered-but-invalid one is classified by type.</summary>
    public static string ForQuestion(Question question, bool isAnswered)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (!isAnswered) return "required";

        return question.Type switch
        {
            QuestionType.Date => "bad_date",
            QuestionType.TextArea => "too_long",
            QuestionType.Autocomplete => "selection_invalid",
            QuestionType.Radio => "selection_invalid",
            // A rejected checkbox value can only mean the option was not among the ones this
            // user was shown — a bad selection, the same kind of failure as the radio's.
            QuestionType.Checkbox => "selection_invalid",
            // AB#296648: a grade picker is a selection control, so it belongs with the other two.
            // Without this it fell through to the generic "invalid", which loses exactly the
            // distinction this taxonomy exists to make — a rejected grade is a bad selection (the
            // qualification does not offer it, or it matches the current grade), not a bad format.
            QuestionType.GradeSelect => "selection_invalid",
            // AB#297848: the syllabus picker is the same kind of control for the same reason — a
            // rejected code means the qualification does not offer it (or offers none at all), which
            // is a bad selection, not a bad format.
            QuestionType.SyllabusSelect => "selection_invalid",
            _ => "invalid",
        };
    }
}
