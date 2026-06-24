using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

public class SummaryListRowViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(
        string title,
        object value,
        string? link = null,
        string? visuallyHiddenText = null,
        string? format = null)
    {
        return View(new SummaryListRowViewModel
        {
            Title = title,
            Value = FormatValue(value, format),
            Link = link,
            VisuallyHiddenText = visuallyHiddenText ?? title.ToLowerInvariant()
        });
    }
    
    private static string FormatValue(object? value, string? format)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(format) && value is IFormattable formattable)
        {
            return formattable.ToString(format, null);
        }

        return value.ToString() ?? string.Empty;
    }
}

public class SummaryListRowViewModel
{
    public required string Title { get; init; }
    public required string Value { get; init; }
    public string? Link { get; init; }
    public string? VisuallyHiddenText { get; init; }
}