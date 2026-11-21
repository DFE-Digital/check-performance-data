namespace DfE.CheckPerformanceData.Persistence.Entities;

// One use of a media library image on one page version or one content block: which image version
// it shows, and where (an Image widget or rich text). Derived from the content each time a page
// version or a block is saved, so a historic page version renders the file it was saved with and
// the library can show where an image is used. Exactly one owner is set: the page version (with
// its page) or the content block. A block has no versions that render, so its rows always follow
// its current value and the newest file.
public sealed class MediaUsage
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public int AssetVersion { get; set; }
    public Guid? PageNodeId { get; set; }
    public Guid? PageNodeVersionId { get; set; }
    public int? ContentBlockId { get; set; }
    public ContentBlock? ContentBlock { get; set; }
    // MediaUsageLocations.Widget or MediaUsageLocations.RichText.
    public string Location { get; set; } = string.Empty;
    // The widget's tree path when Location is widget (or the rich-text widget holding the image).
    public string? WidgetId { get; set; }
}
