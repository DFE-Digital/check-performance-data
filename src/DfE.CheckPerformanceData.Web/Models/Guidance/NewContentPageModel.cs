using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Web.Models.Guidance;

// Create form for a new content page. Slug is optional — it is derived from the title when left blank.
public sealed class NewContentPageModel
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Slug { get; set; }

    public string? Caption { get; set; }

    public string Layout { get; set; } = "nav-content";
}
