namespace DfE.CheckPerformanceData.Application.ContentPages;

// Depth-first, document-order traversal of a content tree. Descends into regions and their columns so
// callers that care about widgets (the auto-nav, the heading anchorizer) see every one regardless of
// how deeply it is nested.
public static class ContentTreeWalker
{
    public static IEnumerable<WidgetNode> AllWidgets(IReadOnlyList<ContentNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case WidgetNode widget:
                    yield return widget;
                    break;
                case RegionNode region:
                    foreach (var column in region.Columns)
                        foreach (var descendant in AllWidgets(column))
                            yield return descendant;
                    break;
            }
        }
    }
}
