namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>The result of starting a journey for a change type.</summary>
/// <param name="Recorded">
/// True when the window type offers the change type. The journey state then holds the selection,
/// and the caller saves it.
/// </param>
/// <param name="FirstPageId">
/// The id of the journey's first page. Null when the start was refused, or when the change type
/// has no flow config.
/// </param>
public sealed record JourneyStart(bool Recorded, string? FirstPageId)
{
    /// <summary>The window type does not offer the change type. Nothing changed.</summary>
    public static JourneyStart Refused { get; } = new(false, null);
}
