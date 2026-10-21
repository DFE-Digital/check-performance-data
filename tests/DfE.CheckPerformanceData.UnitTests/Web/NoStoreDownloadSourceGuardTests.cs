using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// #615 (OWASP A04): every personal-data download is stamped with the one shared NoStore()
// helper. This guard fails if any in-scope action stops calling it, or if a second writer of
// the "no-store" literal appears anywhere in the Web project — the helper file is the only
// place the value may ever be written.
public sealed class NoStoreDownloadSourceGuardTests
{
    // In-scope download actions and how many times each must call the helper. Journey and
    // SubmittedRequest have one evidence route each; CheckYourPupilData has six download
    // actions; Egress, StorageAdmin and AppLogs one each; AuditLogCsv's streamed result one.
    private static readonly Dictionary<string, int> InScopeActions = new()
    {
        ["Controllers/Journey/JourneyController.cs"] = 1,
        ["Controllers/SubmittedRequest/SubmittedRequestController.cs"] = 1,
        ["Controllers/CheckYourPupilData/CheckYourPupilDataController.cs"] = 6,
        ["Controllers/Egress/EgressController.cs"] = 1,
        ["Controllers/StorageAdminController.cs"] = 1,
        ["Controllers/AppLogsController.cs"] = 1,
        ["Controllers/AuditLog/AuditLogCsv.cs"] = 1,
    };

    [Fact]
    public void The_no_store_literal_is_written_only_by_the_helper()
    {
        var offenders = Sweep()
            .Where(file => file.Text.Contains("\"no-store\""))
            .Select(file => file.Relative)
            .ToList();

        Assert.Equal(["Extensions/HttpResponseExtensions.cs"], offenders);
    }

    [Fact]
    public void Every_in_scope_download_action_calls_the_helper_the_expected_number_of_times()
    {
        var byPath = Sweep().ToDictionary(file => file.Relative, file => file.Text);

        var failures = InScopeActions
            .Select(pair =>
            {
                var actual = CountOccurrences(byPath.GetValueOrDefault(pair.Key), ".NoStore()");
                return actual == pair.Value
                    ? null
                    : $"{pair.Key}: expected {pair.Value} call(s) of .NoStore(), found {actual}";
            })
            .Where(failure => failure is not null)
            .ToList();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private sealed record SourceFile(string Relative, string Text);

    private static List<SourceFile> Sweep()
    {
        var web = Path.Combine(RepoRoot, "src", "DfE.CheckPerformanceData.Web");
        var sep = Path.DirectorySeparatorChar.ToString();

        return Directory.GetFiles(web, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{sep}bin{sep}") && !file.Contains($"{sep}obj{sep}"))
            .Select(file => new SourceFile(
                Path.GetRelativePath(web, file).Replace('\\', '/'),
                File.ReadAllText(file)))
            .ToList();
    }

    private static int CountOccurrences(string? text, string token)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
