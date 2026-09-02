namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Renders a <see cref="QuestionType.Checkbox"/> answer for the four places that display a
/// journey answer (the summary page, the request document, the submitted-request view and the
/// admin requests view). It lives here, rather than being pasted into each of them, because the
/// ordering and fallback rules below are the whole behaviour and must not drift apart.
/// </summary>
public static class CheckboxAnswerDisplay
{
    /// <summary>The ticked options' labels, joined for display.</summary>
    public static string Join(Question question, QuestionAnswer? answer) =>
        string.Join(", ", Ordered(question, answer).Select(v => LabelFor(question, v)));

    /// <summary>
    /// The ticked options' raw values, joined — the machine-readable form for the request
    /// document, the exact analogue of an Autocomplete answer's <c>CodeValue</c>.
    /// </summary>
    public static string JoinValues(Question question, QuestionAnswer? answer) =>
        string.Join(",", Ordered(question, answer));

    // Config order, not post order: the browser posts checkboxes in DOM order today, but that is
    // not a guarantee, and a summary that reorders itself between two visits reads as a change
    // the user did not make. A value with no matching option (retired from the config after the
    // answer was stored) keeps its place at the end rather than disappearing from the summary.
    private static IEnumerable<string> Ordered(Question question, QuestionAnswer? answer)
    {
        var selected = answer?.SelectedValues;
        if (selected is not { Count: > 0 }) return [];

        var configured = question.Options?.Select(o => o.Value).ToList() ?? [];
        return selected
            .OrderBy(v => configured.IndexOf(v) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(v => selected.IndexOf(v));
    }

    private static string LabelFor(Question question, string value) =>
        question.Options?.FirstOrDefault(o => o.Value == value)?.Label ?? value;
}
