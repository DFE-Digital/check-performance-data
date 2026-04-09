namespace DfE.CheckPerformanceData.Domain.Entities;

public sealed class WikiPageVersion
{
    public int Id { get; set; }
    public int WikiPageId { get; set; }
    public WikiPage WikiPage { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public int VersionNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
