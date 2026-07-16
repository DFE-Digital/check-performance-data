namespace DfE.CheckPerformanceData.Application.PageTree;

// A page whose current version is live at the moment of the query. Used by
// the help-search resolver to convert a content block's LastSeenPath into a
// safe, published link + page title.
public sealed record PublishedPageInfoDto(string Path, string Title);
