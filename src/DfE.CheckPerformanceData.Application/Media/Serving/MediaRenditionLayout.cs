namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>What a renderer does: take <see cref="Source"/> from the stored image and scale it to Width by Height.</summary>
public sealed record MediaRenditionLayout(MediaPixelRect Source, int Width, int Height);
