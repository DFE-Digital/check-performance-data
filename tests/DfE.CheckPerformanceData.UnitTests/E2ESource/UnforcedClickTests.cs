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
            .SelectMany(file => ForcedLines(File.ReadAllText(file))
                .Select(line => $"{Path.GetRelativePath(folder, file)}:{line}"))
            .ToList();

        Assert.Empty(forced);
    }

    [Theory]
    [InlineData("await radio.CheckAsync(new() { Force = true });")]
    [InlineData("await radio.CheckAsync(new() { Force=true });")]
    [InlineData("await button.ClickAsync(new LocatorClickOptions { Force  =  true });")]
    [InlineData("await radio.CheckAsync(new()\n{\n    Force =\n        true\n});")]
    public void The_guard_finds_a_forced_click_however_it_is_spaced(string code) =>
        Assert.NotEmpty(ForcedLines(code));

    [Theory]
    [InlineData("await radio.CheckAsync(new() { Force = false });")]
    [InlineData("var reForce = true;")]
    [InlineData("var options = new { Force = trueish };")]
    public void The_guard_leaves_other_code_alone(string code) =>
        Assert.Empty(ForcedLines(code));

    // The line reported is the one the option starts on, so a failure points at the right place.
    [Fact]
    public void The_guard_reports_the_line_the_option_starts_on()
    {
        const string code = "// first line\nawait radio.CheckAsync(new()\n{\n    Force =\n        true\n});";

        Assert.Equal([4], ForcedLines(code));
    }

    // Line numbers of every forced option in a file's text. The whole text is matched, not one line
    // at a time, so an option wrapped over several lines is still found. A comment that mentions
    // the option is reported too, which is the safe side for a guard.
    private static IEnumerable<int> ForcedLines(string text) =>
        ForcedOption().Matches(text)
            .Select(match => text.AsSpan(0, match.Index).Count('\n') + 1);

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
