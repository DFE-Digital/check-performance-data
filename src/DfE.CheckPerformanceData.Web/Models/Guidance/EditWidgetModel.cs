using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Web.Models.Guidance;

// Model for the editor's per-widget partial: the widget itself plus the action base (URL prefix
// for the edit form post) and the wire-format path the edit form posts back.
public sealed record EditWidgetModel(string ActionBase, string Path, WidgetNode Widget);
