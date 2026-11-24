namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>A content block the move left as it was, and why.</summary>
public sealed record EmbeddedImageBlockFailure(EmbeddedImageBlock Block, IReadOnlyList<EmbeddedImageRefusal> Reasons);
