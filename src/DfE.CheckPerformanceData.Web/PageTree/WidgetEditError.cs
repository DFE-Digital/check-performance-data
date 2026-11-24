using System.Text.Json;
using DfE.CheckPerformanceData.Application.ContentPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace DfE.CheckPerformanceData.Web.PageTree;

/// <summary>
/// A widget save that was refused, carried across the redirect back to the editor. The editor
/// shows an error summary, opens the widget at <see cref="Path"/>, puts each error beside its
/// field and fills the fields with what the editor posted.
/// </summary>
public sealed record WidgetEditError(string Path, IReadOnlyList<WidgetFieldError> Errors, IReadOnlyDictionary<string, string?> Props)
{
    private const string TempDataKey = "WidgetEditError";

    public void Save(ITempDataDictionary tempData) =>
        tempData[TempDataKey] = JsonSerializer.Serialize(this);

    public static WidgetEditError? Read(ITempDataDictionary tempData) =>
        tempData[TempDataKey] is string json ? JsonSerializer.Deserialize<WidgetEditError>(json) : null;

    /// <summary>The message for one field, or null when it has none.</summary>
    public string? MessageFor(string field) => Errors.FirstOrDefault(e => e.Field == field)?.Message;
}
