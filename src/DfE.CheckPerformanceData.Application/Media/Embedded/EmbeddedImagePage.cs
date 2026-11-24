namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>A page whose current versions may hold embedded images. Path has no leading slash.</summary>
public sealed record EmbeddedImagePage(Guid NodeId, string Title, string Path, string PageType);
