namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>A point of an image as fractions (0 to 1) of its width and height, to 3 decimal places.</summary>
public sealed record MediaFocalPoint(decimal X, decimal Y);
