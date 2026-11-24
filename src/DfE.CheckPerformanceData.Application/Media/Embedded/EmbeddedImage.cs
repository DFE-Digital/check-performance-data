namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>One <c>&lt;img&gt;</c> whose src is a <c>data:</c> URI. Values are as the browser reads them (entities decoded).</summary>
public sealed record EmbeddedImage(string Src, string? Alt, string? Title);
