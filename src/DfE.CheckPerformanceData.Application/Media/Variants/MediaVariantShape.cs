using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Application.Media.Variants;

/// <summary>
/// How a page shows an image: the whole image (<see cref="Original"/>), a cut of it made from the
/// original file when the page renders (<see cref="Cut"/>, with no width), or the box that rich
/// text sets (<see cref="BoxWidth"/> and <see cref="BoxHeight"/>). It is a setting of each use.
/// No file is stored for it.
/// </summary>
public sealed record MediaVariantShape(MediaRendition? Cut, int? BoxWidth, int? BoxHeight)
{
    public static readonly MediaVariantShape Original = new(null, null, null);

    public bool IsOriginal => Cut is null && BoxWidth is null && BoxHeight is null;

    public bool IsRichTextBox => BoxWidth is not null || BoxHeight is not null;
}
