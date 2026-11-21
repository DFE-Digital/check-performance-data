namespace DfE.CheckPerformanceData.Application.Media.Embedded;

/// <summary>An embedded image that cannot move, named by its alt text, its title or its position on the page.</summary>
public sealed record EmbeddedImageRefusal(string Image, string Reason);
