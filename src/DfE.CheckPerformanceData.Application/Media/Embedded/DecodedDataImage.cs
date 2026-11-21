using DfE.CheckPerformanceData.Application.Media.Upload;

namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>
/// The file in a <c>data:</c> URI, or why there is none. <see cref="Format"/> comes from the
/// bytes, never from the type the URI declares.
/// </summary>
public sealed record DecodedDataImage(byte[]? Bytes, MediaFormat? Format, string? Problem);
