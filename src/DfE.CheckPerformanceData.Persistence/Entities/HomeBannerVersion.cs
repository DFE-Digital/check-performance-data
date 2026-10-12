namespace DfE.CheckPerformanceData.Persistence.Entities;

// A snapshot of everything an editor can change on a banner, taken on every save, so a
// restore puts the banner back exactly (#566).
public sealed class HomeBannerVersion
{
    public int Id { get; set; }
    public int HomeBannerId { get; set; }
    public HomeBanner HomeBanner { get; set; } = null!;
    public int VersionNumber { get; set; }
    public string Heading { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime? ShowFrom { get; set; }
    public DateTime? ShowUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}
