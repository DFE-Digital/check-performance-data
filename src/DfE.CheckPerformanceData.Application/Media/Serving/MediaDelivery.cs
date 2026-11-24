namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// The answer to a media request. When <see cref="Outcome"/> is Found, the caller sends
/// <see cref="Content"/> (and disposes it) with <see cref="ContentType"/> and <see cref="ETag"/>.
/// The ETag is quoted, ready for the header. A stored version never changes, so the ETag names
/// the image, the version and, for a rendition, its settings.
/// </summary>
public sealed record MediaDelivery(MediaDeliveryOutcome Outcome, Stream? Content, string? ContentType, long? Length, string? ETag)
{
    public static MediaDelivery NotFound { get; } = new(MediaDeliveryOutcome.NotFound, null, null, null, null);
    public static MediaDelivery Invalid { get; } = new(MediaDeliveryOutcome.Invalid, null, null, null, null);
}
