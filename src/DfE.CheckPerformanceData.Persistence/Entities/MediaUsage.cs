namespace DfE.CheckPerformanceData.Persistence.Entities;

// One use of a media library image on one page version: which image version the page version
// shows, and where (an Image widget or rich text). Derived from the page content each time a page
// version is saved, so a historic page version renders the file it was saved with and the library
// can show where an image is used.
public sealed class MediaUsage
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public int AssetVersion { get; set; }
    public Guid PageNodeId { get; set; }
    public Guid PageNodeVersionId { get; set; }
    // MediaUsageLocations.Widget or MediaUsageLocations.RichText.
    public string Location { get; set; } = string.Empty;
    // The widget's tree path when Location is widget (or the rich-text widget holding the image).
    public string? WidgetId { get; set; }
}
