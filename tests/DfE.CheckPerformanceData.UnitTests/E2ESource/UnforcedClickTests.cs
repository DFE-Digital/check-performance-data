using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.E2ESource;

// A forced click skips Playwright's wait for the element to be visible and to stop moving. Made
// straight after a navigation it can land on a page that is still being laid out, and the E2E test
// fails with "Clicking the checkbox did not change its state" (#584). GOV.UK radios and checkboxes
// take an ordinary click, so the E2E tests never need to force one.
public sealed partial class UnforcedClickTests
{
    [Fact]
    public void No_e2e_test_forces_a_click()
    {
        var folder = Path.Combine(RepoRoot, "tests", "DfE.CheckPerformanceData.E2ETests");
        Assert.True(Directory.Exists(folder), $"The E2E tests are not at {folder}.");

        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(Path.GetRelativePath(folder, file)))
            .ToList();
        // A guard that reads nothing passes for the wrong reason.
        Assert.NotEmpty(files);

        var forced = files
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(x => ForcedOption().IsMatch(x.line))
                .Select(x => $"{Path.GetRelativePath(folder, file)}:{x.number}"))
            .ToList();

        Assert.Empty(forced);
    }

    [Theory]
    [InlineData("await radio.CheckAsync(new() { Force = true });")]
    [InlineData("await radio.CheckAsync(new() { Force=true });")]
    [InlineData("await button.ClickAsync(new LocatorClickOptions { Force  =  true });")]
    public void The_guard_finds_a_forced_click_however_it_is_spaced(string line) =>
        Assert.Matches(ForcedOption(), line);

    [Theory]
    [InlineData("await radio.CheckAsync(new() { Force = false });")]
    [InlineData("var reinforce = true;")]
    public void The_guard_leaves_other_code_alone(string line) =>
        Assert.DoesNotMatch(ForcedOption(), line);

    private static bool IsBuildOutput(string relativePath)
    {
        var top = relativePath.Split(Path.DirectorySeparatorChar)[0];
        return top is "bin" or "obj";
    }

    [GeneratedRegex(@"\bForce\s*=\s*true\b")]
    private static partial Regex ForcedOption();

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
