using System.Text.Json.Nodes;

namespace DfE.CheckPerformanceData.Application.ContentPages;

/// <summary>
/// Checks the props of one widget type when an editor saves the widget. A widget type with no
/// validator saves its props as posted.
/// </summary>
public interface IWidgetPropsValidator
{
    string WidgetType { get; }

    Task<WidgetPropsValidation> ValidateAsync(JsonObject props, CancellationToken ct = default);
}
