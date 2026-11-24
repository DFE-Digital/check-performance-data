namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

/// <summary>A folder in a select list, named by its full path so two folders of one name differ.</summary>
public sealed record MediaFolderOption(Guid Id, string Label);
