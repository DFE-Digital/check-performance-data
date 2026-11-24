namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>A rectangle of a stored image in pixels, measured from its top-left corner.</summary>
public sealed record MediaPixelRect(int X, int Y, int Width, int Height);
