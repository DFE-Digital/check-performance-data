using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Web.Models.Guidance;

// Model for the editor's per-widget partial: the widget itself plus the slug and the wire-format path
// the edit form posts back.
public sealed record EditWidgetModel(string Slug, string Path, WidgetNode Widget);
