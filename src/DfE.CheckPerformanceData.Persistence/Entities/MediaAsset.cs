namespace DfE.CheckPerformanceData.Persistence.Entities;

// An image in the CMS media library. Pages refer to it by Id, never by file. Replacing the image
// adds a MediaAssetVersion and moves CurrentVersion on, so live and draft pages show the new file
// while a historic page version still resolves the version it was saved with.
public sealed class MediaAsset
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    // URL-safe name used in the file's public URL. Not unique: the id identifies the image.
    public string Slug { get; set; } = string.Empty;
    public string? DefaultAltText { get; set; }
    public string? Caption { get; set; }
    public string? Credit { get; set; }
    public List<string> Tags { get; set; } = [];
    public int CurrentVersion { get; set; }
    // The folder that holds the image, or null for the top of the library.
    public Guid? FolderId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
    public ICollection<MediaAssetVersion> Versions { get; set; } = [];
}
