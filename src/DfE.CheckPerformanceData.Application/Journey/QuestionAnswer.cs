namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class QuestionAnswer
{
    public string? TextValue { get; set; }

    /// <summary>
    /// For <c>Autocomplete</c> questions: the selected option's stable code
    /// (e.g. ISO country code), captured from the <c>{field}_code</c> hidden input.
    /// <see cref="TextValue"/> holds the display name the user saw.
    /// </summary>
    public string? CodeValue { get; set; }

    /// <summary>
    /// For <c>Checkbox</c> questions: the ticked options' values. Null or empty means
    /// nothing was ticked. <see cref="TextValue"/> stays null for a checkbox list, so no
    /// display site can mistake a single stray value for a selection.
    /// </summary>
    public List<string>? SelectedValues { get; set; }

    public DateAnswer? DateValue { get; set; }
    public List<FileAnswer>? FileValues { get; set; }
}
