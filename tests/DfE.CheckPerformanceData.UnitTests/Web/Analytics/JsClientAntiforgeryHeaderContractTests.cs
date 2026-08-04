using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Analytics;

// Contract test that pins the antiforgery header name used by every browser-side fetch call.
// Program.cs configures Antiforgery with HeaderName = "X-XSRF-TOKEN"; the framework default
// name "RequestVerificationToken" is a silent 400 for every DELETE / POST that reaches the
// pipeline via fetch(). That mismatch has already cost one round of debug time — this test
// regex-scans every .js file under wwwroot/js and fails if any fetch-body headers object
// uses the wrong header name for the antiforgery token.
//
// Only bare 'RequestVerificationToken' matches are flagged — the form-field name
// '__RequestVerificationToken' (double underscore) is the legitimate hidden-input name and
// stays permitted. The scan also skips full-line comments so the test doesn't fire on
// prose that mentions the wrong name.
public sealed class JsClientAntiforgeryHeaderContractTests
{
    // Two shapes that both indicate "used as a header key":
    //   headers['RequestVerificationToken']   -> bracket indexer
    //   'RequestVerificationToken':           -> object-literal key (may also be double-quoted)
    // Requires the identifier to NOT be preceded by an underscore so the form-field name
    // __RequestVerificationToken (double underscore) is not flagged.
    private static readonly Regex BracketIndexer = new(
        @"(?<![_A-Za-z0-9$])headers\s*\[\s*['""]RequestVerificationToken['""]\s*\]",
        RegexOptions.Compiled);

    private static readonly Regex ObjectLiteralKey = new(
        @"(?<![_A-Za-z0-9$])['""]RequestVerificationToken['""]\s*:",
        RegexOptions.Compiled);

    [Fact]
    public void EveryFetchCall_UsesXXsrfTokenHeader_NotFrameworkDefault()
    {
        var jsDir = LocateWwwrootJsDirectory();
        var files = Directory.EnumerateFiles(jsDir, "*.js", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(files);

        var offenders = new List<string>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Skip full-line comments so a line like `// don't use RequestVerificationToken`
                // does not fail the scan.
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (BracketIndexer.IsMatch(line) || ObjectLiteralKey.IsMatch(line))
                {
                    offenders.Add(
                        $"{Path.GetFileName(file)}:{i + 1}: {trimmed}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Every browser-side fetch header carrying the antiforgery token must use " +
            "'X-XSRF-TOKEN' (matches Program.cs's AddAntiforgery configuration). " +
            "The framework default 'RequestVerificationToken' name causes a silent 400 " +
            "on every DELETE / POST that reaches the pipeline via fetch. Offenders:\n  " +
            string.Join("\n  ", offenders));
    }

    // Walks up from this file's directory to the repository root (the folder holding the
    // Makefile) then descends into src/DfE.CheckPerformanceData.Web/wwwroot/js. Mirrors the
    // AppSettingsSecretsTests approach — uses CallerFilePath so the walk survives whatever
    // AppContext.BaseDirectory the test host happens to pick.
    private static string LocateWwwrootJsDirectory([CallerFilePath] string testFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Makefile")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate repository root (no Makefile found on the way up).");
        }
        return Path.Combine(
            dir.FullName, "src", "DfE.CheckPerformanceData.Web", "wwwroot", "js");
    }
}
