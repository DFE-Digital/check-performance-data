using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// Flattens a journey's answers to strings keyed by question id. Dates become yyyy-MM-dd (the
/// journey blob stores DateAnswer parts, never an ISO string — docs/add-pupil-journey.md), an
/// autocomplete's stable code wins over its label, checkbox values join with "|", and a blank
/// answer is omitted so callers can use "missing" and "empty" interchangeably.
/// </summary>
public static class EgressAnswers
{
    public static IReadOnlyDictionary<string, string> Flatten(RequestState state)
    {
        var flat = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (questionId, answer) in state.QuestionAnswers)
        {
            var value = ValueOf(answer);
            if (!string.IsNullOrWhiteSpace(value))
                flat[questionId] = value.Trim();
        }
        return flat;
    }

    private static string? ValueOf(QuestionAnswer answer)
    {
        if (answer.DateValue is { } date)
            return date.ToDateOnly()?.ToString("yyyy-MM-dd");
        if (!string.IsNullOrWhiteSpace(answer.CodeValue))
            return answer.CodeValue;
        if (answer.SelectedValues is { Count: > 0 } selected)
            return string.Join("|", selected);
        return answer.TextValue;
    }
}
