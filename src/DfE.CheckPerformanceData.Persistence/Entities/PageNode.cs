namespace DfE.CheckPerformanceData.Persistence.Entities;

// The single hierarchy for all CMS pages. Self-referencing (ParentId), GUID-keyed. Path is the
// materialised full slug path, kept unique among live nodes for O(1) route resolution.
public sealed class PageNode
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Segment { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string Title { get; set; } = string.Empty;

    // Optional lede rendered above the page's H1 (design calls it a "subtitle"). Never used for
    // routing, search or nav — purely a display string on the rendered page.
    public string? Subtitle { get; set; }

    // Optional label used in the admin left-hand navigation and other tree/list surfaces when the
    // page's actual Title would be too long, too formal or duplicated. Falls back to Title when
    // unset. Never used for the rendered page's H1 or for routing.
    public string? PageName { get; set; }

    public string PageType { get; set; } = "folder"; // content | wiki | folder

    // Public-facing menu visibility. false hides this page from PageNav mode=children (the
    // side-nav on content pages that lists sibling / child pages). The admin left-hand tree
    // still shows the page — hiding it from admin would strand any editor who wanted to
    // find it. Default true so existing pages stay visible.
    public bool ShowInMenu { get; set; } = true;

    public DateTime CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedDate { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string? DeletedBy { get; set; }
    public ICollection<PageNodeVersion> Versions { get; set; } = [];
}
