namespace DfE.CheckPerformanceData.Application.ContentPages;

/// <summary>One problem with a widget prop that an editor posted. <see cref="Field"/> is the prop name.</summary>
public sealed record WidgetFieldError(string Field, string Message);
