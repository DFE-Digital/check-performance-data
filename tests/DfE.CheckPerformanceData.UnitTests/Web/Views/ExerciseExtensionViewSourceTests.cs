using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Views;

// AB#307773 review finding 5: the no-data tick travels with the schools it was given for.
public sealed class ExerciseExtensionViewSourceTests
{
    private static string Read(string view) =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Views", "WindowAdmin", view));

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    [Fact]
    public void The_warning_posts_the_schools_the_tick_was_given_for()
    {
        var source = Read("_ExtensionWarning.cshtml");

        Assert.Contains("<input type=\"hidden\" name=\"ConfirmedSchools\" value=\"@Model.ConfirmedSchools\" />", source);
        Assert.Contains("name=\"ConfirmNoData\"", source);
    }
}
