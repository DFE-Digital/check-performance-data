namespace DfE.CheckPerformanceData.Application.HomeBanners;

public sealed class HomeBannerVersionDto
{
    public int Id { get; init; }
    public int HomeBannerId { get; init; }
    public int VersionNumber { get; init; }
    public string Heading { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string? BodyHtml { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime? ShowFrom { get; init; }
    public DateTime? ShowUntil { get; init; }
    /// <summary>UK wall-clock, for display: the service converts the repository's UTC stamp.</summary>
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }

    public HomeBannerContent ToContent() => new(Heading, Body, IsEnabled, ShowFrom, ShowUntil);
}
