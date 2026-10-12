using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.HomeBanners;

public sealed class HomeBannerDto
{
    public int Id { get; init; }
    public Guid ContentId { get; init; }
    public string Heading { get; init; } = string.Empty;
    /// <summary>Sanitised HTML as stored.</summary>
    public string Body { get; init; } = string.Empty;
    /// <summary>Body passed through the sanitiser again for rendering; set by the service.</summary>
    public string? BodyHtml { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime? ShowFrom { get; init; }
    public DateTime? ShowUntil { get; init; }
    public int SortOrder { get; init; }
    /// <summary>Computed by the service against the UK clock; Off until the service sets it.</summary>
    public HomeBannerStatus Status { get; init; }
    /// <summary>UK wall-clock, for display: the service converts the repository's UTC stamp.</summary>
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    /// <summary>UK wall-clock, for display: the service converts the repository's UTC stamp.</summary>
    public DateTime UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }

    public HomeBannerContent ToContent() => new(Heading, Body, IsEnabled, ShowFrom, ShowUntil);
}
