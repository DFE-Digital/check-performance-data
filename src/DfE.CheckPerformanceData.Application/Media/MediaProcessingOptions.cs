namespace DfE.CheckPerformanceData.Application.Media;

/// <summary>Settings for decoding images, bound from the Media configuration section.</summary>
public sealed class MediaProcessingOptions
{
    public const string SectionName = "Media";

    /// <summary>
    /// How many images may be decoded at once, across uploads and resized copies. A 50 megapixel
    /// image needs about 400 MB while it is decoded, so this caps the memory that decoding uses.
    /// </summary>
    public int MaxConcurrentProcessing { get; set; } = 2;
}
