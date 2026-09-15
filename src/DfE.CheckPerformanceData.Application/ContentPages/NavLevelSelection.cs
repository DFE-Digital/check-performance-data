namespace DfE.CheckPerformanceData.Application.ContentPages;

// Reads the page-nav widget's per-level tick boxes into the set of heading levels its contents
// list should show.
//
// A widget placed before the tick boxes existed carries none of these props. That is not the
// same as an author unticking everything: the first means "never asked", which is the default
// pair, and the second means "asked for none", which is an empty list. Telling them apart is
// why this checks for the props' presence rather than just reading each one as a boolean.
public static class NavLevelSelection
{
    public static IReadOnlyCollection<int> For(WidgetNode widget)
    {
        var anyDeclared = false;
        var levels = new List<int>();

        for (var level = 1; level <= 6; level++)
        {
            var key = $"h{level}";
            if (widget.Props?.ContainsKey(key) != true) continue;

            anyDeclared = true;
            if (widget.GetBool(key) == true) levels.Add(level);
        }

        return anyDeclared ? levels : ContentNavBuilder.DefaultLevels;
    }
}
