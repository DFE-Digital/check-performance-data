namespace DfE.CheckPerformanceData.Persistence.Entities;

// A notification banner on the anonymous start page (#566). Editors manage these in the CMS
// admin; the start page shows the ones that are on and within their dates, in SortOrder.
public sealed class HomeBanner
{
    public int Id { get; set; }
    // Stable cross-environment identity, preserved through content-staging export/import so the
    // same banner is recognised across environments (see ContentBlock.ContentId).
    public Guid ContentId { get; set; }
    public string Heading { get; set; } = string.Empty;
    // Sanitised HTML (IHtmlRenderingService.RenderHtml) — never raw editor input.
    public string Body { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    // UK wall-clock times, no zone (timestamp without time zone), like CheckingExercise dates.
    // Null means "no limit". ShowFrom is inclusive, ShowUntil exclusive.
    public DateTime? ShowFrom { get; set; }
    public DateTime? ShowUntil { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public ICollection<HomeBannerVersion> Versions { get; set; } = [];
}
