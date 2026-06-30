namespace DfE.CheckPerformanceData.Application.ContentPages;

// Regenerates every heading widget's anchor from its text, in document order, allocating page-unique
// slugs. Run after each edit so anchors always match the headings the auto-nav links to — editors
// never set anchors by hand.
public static class HeadingAnchorizer
{
    public static void Apply(IReadOnlyList<ContentNode> tree)
    {
        var allocator = new AnchorAllocator();
        foreach (var heading in ContentTreeWalker.AllWidgets(tree).Where(w => w.Type == "heading"))
            heading.Anchor = allocator.Allocate(heading.GetString("text"));
    }
}
