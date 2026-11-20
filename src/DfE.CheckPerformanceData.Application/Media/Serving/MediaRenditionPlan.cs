namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>What a renderer does: take <see cref="Source"/> from the stored image and scale it to Width by Height.</summary>
public sealed record MediaRenditionPlan(MediaPixelRect Source, int Width, int Height);
