using System.Text.Json.Nodes;

namespace DfE.CheckPerformanceData.Application.ContentPages;

/// <summary>
/// The result of checking a widget's props before they are saved. <see cref="Props"/> holds the
/// values to save, which can differ from the posted ones (for example, a decorative image saves an
/// empty alt text). When <see cref="Errors"/> is not empty, nothing is saved.
/// </summary>
public sealed record WidgetPropsValidation(JsonObject Props, IReadOnlyList<WidgetFieldError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
