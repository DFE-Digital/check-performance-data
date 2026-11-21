namespace DfE.CheckPerformanceData.Application.Media.Reports;

/// <summary>How many resized copies are stored, and their size in bytes.</summary>
public sealed record MediaRenditionTally(int Count, long Bytes)
{
    public static readonly MediaRenditionTally None = new(0, 0);
}
