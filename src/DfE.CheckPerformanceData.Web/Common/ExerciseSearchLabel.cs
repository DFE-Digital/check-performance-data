using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Common;

/// <summary>
/// The label of an exercise tab's search box. The search matches the selected dataset's searchable
/// columns (<c>x-display.searchable</c> in its schema), so the label names those columns, in
/// display order: "Search for a pupil by last name, first name or UPN". A label that named fixed
/// fields would hide a column the schema made searchable, or promise one it did not.
/// </summary>
public static class ExerciseSearchLabel
{
    public static string For(LearnerNoun noun, ExerciseDataset dataset) =>
        For(noun, [.. dataset.Columns.Where(c => c.Searchable).Select(c => c.Label)]);

    public static string For(LearnerNoun noun, IReadOnlyList<string> labels)
    {
        var names = labels.Select(InSentence).ToList();
        return names.Count switch
        {
            0 => $"Search for a {noun.Singular}",
            1 => $"Search for a {noun.Singular} by {names[0]}",
            _ => $"Search for a {noun.Singular} by {string.Join(", ", names[..^1])} or {names[^1]}"
        };
    }

    // "Last name" reads "last name" inside a sentence; an abbreviation (UPN, CYPMD ID) keeps its capitals.
    private static string InSentence(string label)
    {
        var firstWord = label.Split(' ')[0];
        return firstWord.Length > 1 && firstWord.All(c => !char.IsLower(c))
            ? label
            : char.ToLowerInvariant(label[0]) + label[1..];
    }
}
