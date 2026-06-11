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

    public DateAnswer? DateValue { get; set; }
    public List<FileAnswer>? FileValues { get; set; }
}
