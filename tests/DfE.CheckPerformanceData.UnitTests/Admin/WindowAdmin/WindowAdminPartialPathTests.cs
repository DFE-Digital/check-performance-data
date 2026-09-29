using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin.WindowAdmin;

// Views/WindowAdmin is shared by many controllers (SummaryController, CreateCheckingExerciseController,
// ...) that render by full path. MVC finds a partial named without a path only in the *controller's*
// folder and in Views/Shared, never beside the view, so "_ExerciseStatusTag" threw on the summary
// page while the windows list (WindowAdminController) rendered it. Unit tests render no views, so
// this pins the rule in the markup.
public sealed partial class WindowAdminPartialPathTests
{
    [Fact]
    public void Every_window_admin_partial_is_named_by_full_path()
    {
        var folder = Path.Combine(RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "WindowAdmin");
        var localPartials = Directory.GetFiles(folder, "_*.cshtml")
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet();

        // A bare name is fine for a Views/Shared partial; only one that means a file here is wrong.
        var bare = Directory.GetFiles(folder, "*.cshtml")
            .SelectMany(file => PartialName().Matches(File.ReadAllText(file))
                .Select(m => m.Groups["name"].Value)
                .Where(localPartials.Contains)
                .Select(name => $"{Path.GetFileName(file)}: {name}"))
            .ToList();

        Assert.Empty(bare);
    }

    [GeneratedRegex(@"(?:<partial\s+name=|Partial(?:Async)?\()\s*""(?<name>[^""]+)""")]
    private static partial Regex PartialName();

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
