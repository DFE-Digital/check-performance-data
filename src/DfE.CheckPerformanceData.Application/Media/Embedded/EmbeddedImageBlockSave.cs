namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// Everything saved for one content block in one go: its new value, and what the audit log
/// records (how many images moved, and the library images used). The change is made only while
/// the block still holds the value it was read with (<see cref="EmbeddedImageBlock.Content"/>).
/// </summary>
public sealed record EmbeddedImageBlockSave(
    EmbeddedImageBlock Block,
    string Content,
    int ImagesMoved,
    IReadOnlyList<Guid> AssetIds);
