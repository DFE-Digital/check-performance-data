namespace DfE.CheckPerformanceData.Application.Journey;

public sealed class JourneyPage
{
    public required string Id { get; init; }
    public PageType Type { get; init; } = PageType.Question;
    public string? Title { get; init; }
    public string? Subheading { get; init; }
    public string? Content { get; init; }
    public List<Question> Questions { get; init; } = [];
    public string? NextPageId { get; init; }
}
