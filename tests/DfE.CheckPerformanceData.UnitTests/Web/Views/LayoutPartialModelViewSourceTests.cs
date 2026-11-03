using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// A layout renders every page that uses it, so the layout's Model is whatever that page's model
// is. A partial rendered from a layout with no model attribute inherits that page model. If the
// partial declares its own @model type, the first page whose model is a different type throws
// "The model item passed into the ViewDataDictionary is of type ..." and the whole page is a 500.
// That is how the share link broke in #761: _ShareLayout rendered <partial name="_DfeHeader" />
// and _DfeHeader declares @model IHtmlContent?. _Layout and _AdminLayout pass model="accountNav(null)".
// Unit tests render no views, so this pins the rule in the markup: a layout that renders a partial
// with an @model directive must pass a model (model="..." or for="..."), even when that model is null.
public sealed partial class LayoutPartialModelViewSourceTests
{
    [Fact]
    public void Every_layout_passes_a_model_to_each_partial_that_declares_one()
    {
        var shared = Path.Combine(RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "Shared");
        var layouts = Directory.GetFiles(shared, "_*Layout.cshtml");
        Assert.NotEmpty(layouts);

        var offenders = new List<string>();
        foreach (var layout in layouts)
        {
            var source = File.ReadAllText(layout);

            var partialsWithoutModel = PartialTag().Matches(source)
                .Where(m => !ModelOrFor().IsMatch(m.Groups["attrs"].Value))
                .Select(m => m.Groups["name"].Value)
                .Concat(PartialCall().Matches(source).Select(m => m.Groups["name"].Value))
                .Distinct();

            foreach (var name in partialsWithoutModel)
            {
                // Only a Views/Shared partial can be judged from here; a path into another folder
                // is skipped rather than guessed.
                var partial = Path.Combine(shared, Path.GetFileNameWithoutExtension(name) + ".cshtml");
                if (File.Exists(partial) && ModelDirective().IsMatch(File.ReadAllText(partial)))
                    offenders.Add($"{Path.GetFileName(layout)}: {name}");
            }
        }

        Assert.Empty(offenders);
    }

    // <partial name="_X" ... /> : attrs is everything between the tag name and the closing bracket.
    [GeneratedRegex(@"<partial\s+(?<attrs>[^>]*?name=""(?<name>[^""]+)""[^>]*?)/?>")]
    private static partial Regex PartialTag();

    [GeneratedRegex(@"\b(?:model|for)\s*=")]
    private static partial Regex ModelOrFor();

    // Html.Partial("_X") or Html.PartialAsync("_X") with no model argument.
    [GeneratedRegex(@"Partial(?:Async)?\(\s*""(?<name>[^""]+)""\s*\)")]
    private static partial Regex PartialCall();

    [GeneratedRegex(@"^\s*@model\s+", RegexOptions.Multiline)]
    private static partial Regex ModelDirective();

    // This file is tests/DfE.CheckPerformanceData.UnitTests/Web/Views/<this>.cs: the repo root is
    // four levels up. CallerFilePath, not AppContext.BaseDirectory, so `dotnet test -o` still works.
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
