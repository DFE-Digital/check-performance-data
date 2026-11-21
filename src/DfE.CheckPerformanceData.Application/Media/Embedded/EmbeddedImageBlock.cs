namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>A content block whose value may hold embedded images. Content is its stored value.</summary>
public sealed record EmbeddedImageBlock(int Id, string Key, string Content);
