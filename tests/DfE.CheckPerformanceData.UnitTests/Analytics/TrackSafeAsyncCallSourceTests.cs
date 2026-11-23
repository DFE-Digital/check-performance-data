using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

/// <summary>
/// <c>TrackSafeAsync</c> catches every exception. NSubstitute reports a failed <c>Received</c> or
/// <c>DidNotReceive</c> check with an exception, so a check made through <c>TrackSafeAsync</c>
/// cannot fail (#870). A test makes each check and each stub on <c>IAnalyticsService.TrackAsync</c>.
/// Only the tests of <c>TrackSafeAsync</c> itself call it.
/// </summary>
public sealed partial class TrackSafeAsyncCallSourceTests
{
    private const string SourceFilePattern = "*.cs";
    private const string ParentFolder = "..";
    private const string TestsOfTrackSafeAsync = "AnalyticsServiceExtensionsTests.cs";
    private static readonly string[] BuildOutputFolders = ["bin", "obj"];

    [Fact]
    public void Only_its_own_tests_call_TrackSafeAsync()
    {
        Assert.True(Directory.Exists(TestsFolder), $"The test projects are not at {TestsFolder}.");

        var files = Directory.GetFiles(TestsFolder, SourceFilePattern, SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file)
                && !IsNamed(file, TestsOfTrackSafeAsync)
                && !IsNamed(file, ThisFilePath()))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(files);

        var calls = files
            .SelectMany(file => CallLines(File.ReadAllText(file))
                .Select(line => $"{Path.GetRelativePath(TestsFolder, file)}:{line}"))
            .ToList();

        Assert.True(calls.Count == 0,
            $"Use TrackAsync in each of these lines:{Environment.NewLine}{string.Join(Environment.NewLine, calls)}");
    }

    [Theory]
    [InlineData("await _analytics.Received(1).TrackSafeAsync(Arg.Any<AnalyticsEvent>());")]
    [InlineData("await _analytics.DidNotReceive().TrackSafeAsync(sent);")]
    [InlineData("await _analytics.DidNotReceiveWithAnyArgs()\n    .TrackSafeAsync(sent);")]
    [InlineData("await _analytics.Received(Quantity.Exactly(Count(2))).TrackSafeAsync(sent);")]
    [InlineData("await _analytics.Received<IAnalyticsService>(1).TrackSafeAsync(sent);")]
    [InlineData("await _analytics.Received(1)!.TrackSafeAsync(sent);")]
    [InlineData("var check = _analytics.Received(1);\nawait check.TrackSafeAsync(sent);")]
    [InlineData("Received.InOrder(() => _analytics.TrackSafeAsync(sent));")]
    [InlineData("_analytics.TrackSafeAsync(Arg.Any<AnalyticsEvent>()).Returns(Task.CompletedTask);")]
    [InlineData("await AnalyticsServiceExtensions.TrackSafeAsync(_analytics, sent);")]
    [InlineData("await analytics . TrackSafeAsync (sent);")]
    public void The_guard_finds_a_call_however_it_is_written(string code) =>
        Assert.NotEmpty(CallLines(code));

    [Theory]
    [InlineData("await _analytics.Received(1).TrackAsync(sent, Arg.Any<CancellationToken>());")]
    [InlineData("await _analytics.DidNotReceive().TrackAsync(Arg.Any<AnalyticsEvent>(), Arg.Any<CancellationToken>());")]
    [InlineData("// TrackSafeAsync is meant to swallow, but the journey must not depend on that.")]
    [InlineData("public async Task TrackSafeAsync_forwards_to_TrackAsync()")]
    [InlineData("var name = nameof(AnalyticsServiceExtensions.TrackSafeAsync);")]
    public void The_guard_leaves_other_code_alone(string code) =>
        Assert.Empty(CallLines(code));

    [Fact]
    public void The_guard_reports_the_line_of_the_call()
    {
        const string code = "// first line\nawait _analytics.Received(1)\n    .TrackSafeAsync(sent);";

        Assert.Equal([3], CallLines(code));
    }

    private static IEnumerable<int> CallLines(string text) =>
        CallOfTrackSafeAsync().Matches(text)
            .Select(match => text.AsSpan(0, match.Index).Count('\n') + 1);

    private static bool IsBuildOutput(string file) =>
        Path.GetRelativePath(TestsFolder, file)
            .Split(Path.DirectorySeparatorChar)
            .Any(folder => BuildOutputFolders.Contains(folder));

    private static bool IsNamed(string file, string other) =>
        string.Equals(Path.GetFileName(file), Path.GetFileName(other), StringComparison.Ordinal);

    [GeneratedRegex(@"\.\s*TrackSafeAsync\s*\(")]
    private static partial Regex CallOfTrackSafeAsync();

    private static string TestsFolder =>
        Path.GetFullPath(Path.Combine(ThisFilePath(), ParentFolder, ParentFolder, ParentFolder));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
