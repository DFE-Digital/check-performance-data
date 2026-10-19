using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.E2ESource;

// A forced click skips Playwright's wait for the element to be visible and to stop moving. Made
// straight after a navigation it can land on a page that is still being laid out, and the E2E test
// fails with "Clicking the checkbox did not change its state" (#584). GOV.UK radios and checkboxes
// take an ordinary click, so the E2E tests never need to force one.
public sealed class UnforcedClickTests
{
    [Fact]
    public void No_e2e_test_forces_a_click()
    {
        var folder = Path.Combine(RepoRoot, "tests", "DfE.CheckPerformanceData.E2ETests");
        var forced = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(x => x.line.Contains("Force = true"))
                .Select(x => $"{Path.GetRelativePath(folder, file)}:{x.number}"))
            .ToList();

        Assert.Empty(forced);
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
