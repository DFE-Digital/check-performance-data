using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// Stops new code from naming a page id or question id from a shipped question flow. Code that
/// names an id makes that part of the journey impossible to edit from an admin screen: the screen
/// cannot show the rule, and a renamed or deleted id stops the rule with no error.
/// See docs/journey-config-first.md for the rule and for the patterns that replace such code.
///
/// The test reads every page and question id from Web/Data/QuestionFlows/*.json, then finds each
/// string literal in src/**/*.cs and src/**/*.cshtml that is equal to one of them (comments are
/// ignored). It compares the result with <see cref="Baseline"/>, which is a ratchet:
/// a new id in a file fails, and a baseline id that the file no longer names also fails, so the
/// list can only become smaller. Option values are not checked: "yes", "other" and "1" occur too
/// often in code that has no relation to the flows.
/// </summary>
public sealed class FlowIdCouplingTests
{
    /// <summary>
    /// The coupling that existed when the guard was added (2026-10-26). Do not add to it to make the
    /// build pass: use a pattern from docs/journey-config-first.md. If an entry is truly necessary,
    /// add it with a comment that says why.
    /// </summary>
    private static readonly Dictionary<string, string[]> Baseline = new(StringComparer.Ordinal)
    {
        // ── 1. Integration maps: what an answer means to the rules engine, egress or Zendesk.
        //    Replace with question roles and option codes (patterns 4 and 5).
        //    "date-added-to-roll" maps the KS2 roll date to a rules-engine field.
        ["src/DfE.CheckPerformanceData.Application/RulesEngine/AnswerFieldMap.cs"] = ["country-originally-from", "date-added-to-roll", "date-permanently-excluded", "date-pupil-arrived-in-england", "date-pupil-excluded", "date-pupil-started", "date-pupil-started-school-in-england", "date-removed-from-roll", "first-language", "higher-lower", "life-limiting-illness-health-issue", "not-on-roll-reason", "other", "sat-exams", "social-care-reason", "why-removed"],
        ["src/DfE.CheckPerformanceData.Application/Egress/EgressRecordBuilder.cs"] = ["admission-date", "date-of-birth", "first-name", "higher-lower", "last-name", "not-on-roll-reason", "reason", "sex", "upn", "year-group", "year-group-higher-moved-to", "year-group-lower-moved-to", "years-to-remove"],
        ["src/DfE.CheckPerformanceData.Application/Egress/CorrectionCodes.cs"] = ["not-on-roll", "other", "student-died"],
        ["src/DfE.CheckPerformanceData.Application/ZendeskClient/ZendeskTicketFieldOptions.cs"] = ["child-missing-education", "completed-ks4-elsewhere", "dual-registered-moved", "elective-home-education", "english-not-first-language", "life-limiting-illness", "not-on-roll", "other", "permanent-exclusion", "permanently-excluded", "pupil-died", "student-died"],
        ["src/DfE.CheckPerformanceData.RulesEngineWorker/Consumers/ZendeskConsumer.cs"] = ["not-on-roll", "not-on-roll-reason", "other"],
        ["src/DfE.CheckPerformanceData.Web/Controllers/Egress/EgressViewModels.cs"] = ["admission-date", "date-of-birth", "first-name", "last-name", "reason", "sen-status", "sex", "upn", "year-group"],

        // ── 2. Validation in code. Replace with date rules declared on the question (pattern 3).
        ["src/DfE.CheckPerformanceData.Application/Journey/DateRules/AddJourneyDateRules.cs"] = ["admission-date", "admission-details", "date-of-birth", "learner-details"],
        ["src/DfE.CheckPerformanceData.Application/Journey/DateRules/MissingQualificationDateRules.cs"] = ["q-award-date", "qualification-details"],
        ["src/DfE.CheckPerformanceData.Application/Journey/DateRules/PageDateRules.cs"] = ["date-pupil-arrived-in-england", "date-pupil-started", "date-pupil-started-school-in-england", "english-not-first-language-details"],
        //    "added-after-summer-term" and "date-added-to-roll" hold the KS2 roll-date rule.
        ["src/DfE.CheckPerformanceData.Application/Journey/DateRules/RemovalJourneyDateRules.cs"] = ["added-after-summer-term", "child-missing-education", "date-added-to-roll", "date-permanently-excluded", "date-pupil-excluded", "date-removed-from-roll", "elective-home-education", "permanent-exclusion", "permanently-excluded", "permanently-left-england-questions", "pupil-died", "student-died"],

        // ── 3. Single-journey conditions. Replace with branching or a generic answer condition
        //    (patterns 1 and 2).
        ["src/DfE.CheckPerformanceData.Application/Journey/OriginCountryLanguageCapture.cs"] = ["country-originally-from"],

        // ── 4. Journey code in the engine and the Web layer. Replace with question roles
        //    (pattern 4) and page types.
        ["src/DfE.CheckPerformanceData.Application/AmendmentRequests/EditAdviceService.cs"] = ["evidence", "reason"],
        ["src/DfE.CheckPerformanceData.Web/Controllers/Journey/AddPupilJourney.cs"] = ["admission-details", "date-of-birth", "first-name", "last-name", "learner-details", "sex", "upn"],
        ["src/DfE.CheckPerformanceData.Web/Controllers/Journey/JourneyController.cs"] = ["q-cohort-scope", "q-missing-grade", "q-revised-grade", "q-syllabus-code"],
        ["src/DfE.CheckPerformanceData.Web/Controllers/Journey/JourneyViewModelBuilder.cs"] = ["additional-info", "q-additional-info", "q-award-date", "q-cohort-count", "q-cohort-scope", "q-missing-grade", "q-ncn", "q-syllabus-code", "qualification-details", "select-qualification"],
        ["src/DfE.CheckPerformanceData.Web/Controllers/ResultIssue/ResultIssueController.cs"] = ["check-late-results"],
        ["src/DfE.CheckPerformanceData.Web/Views/Journey/AlreadyIncluded.cshtml"] = ["select-pupil"],
        ["src/DfE.CheckPerformanceData.Web/Views/Journey/DuplicateCheck.cshtml"] = ["learner-details"],

        // ── Dev tools and seed data. They write answers for a known journey, and are not on the
        //    path to an editor. They follow the roles when the code they feed moves.
        ["src/DfE.CheckPerformanceData.Web/Controllers/DevEgressController.cs"] = ["admission-date", "date-of-birth", "date-removed-from-roll", "first-name", "last-name", "pupil-died", "reason", "sen-status", "sex", "upn", "year-group"],
        ["src/DfE.CheckPerformanceData.Web/Controllers/OutcomePresets.cs"] = ["first-language"],
        ["src/DfE.CheckPerformanceData.Web/Seeding/SeedChangeRequests.cs"] = ["dual-registered-moved", "dual-registered-moved-dfe-number", "dual-registered-moved-evidence", "reason", "select-pupil"],

        // ── Same text, different meaning: not coupling.
        // Guidance section anchors, not flow pages.
        ["src/DfE.CheckPerformanceData.Web/Models/Guidance/GuidancePage.cs"] = ["child-missing-education", "elective-home-education"],
        // A database column name.
        ["src/DfE.CheckPerformanceData.Persistence/Configurations/DeadLetterConfiguration.cs"] = ["reason"],
    };

    [Fact]
    public void NoSourceFile_NamesAFlowId_ThatIsNotInTheBaseline()
    {
        var added = Scan.Value
            .SelectMany(file => file.Value
                .Where(id => !Baseline.TryGetValue(file.Key, out var allowed) || !allowed.Contains(id))
                .Select(id => (File: file.Key, Id: id)))
            .ToList();

        Assert.True(added.Count == 0,
            "These files name a page or question id from a question flow. Code that names a flow id " +
            "cannot be edited from the admin screen. Use a pattern from docs/journey-config-first.md " +
            "(branch on the answer, a generic answer condition, a declared date rule, or a question " +
            "role) instead.\n\n" + Describe(added) +
            "\n\nIf the coupling is truly necessary, add these lines to the Baseline with a reason:\n" +
            BaselineLines(added));
    }

    [Fact]
    public void EveryBaselineEntry_IsStillNamedByItsFile()
    {
        var stale = Baseline
            .SelectMany(file => file.Value
                .Where(id => !Scan.Value.TryGetValue(file.Key, out var found) || !found.Contains(id))
                .Select(id => (File: file.Key, Id: id)))
            .ToList();

        Assert.True(stale.Count == 0,
            "The code no longer names these ids. Remove them from the Baseline, so it cannot hide new " +
            "coupling later:\n\n" + Describe(stale));
    }

    [Fact]
    public void TheScan_FindsTheFlowsAndTheSource()
    {
        // Without this, a wrong path makes both facts above pass with nothing scanned.
        Assert.Contains("reason", FlowIds.Value);
        Assert.Contains("evidence", FlowIds.Value);
        Assert.True(SourceFiles().Count() > 100, "The scan found almost no source files under src/.");
    }

    // ── The lexer: only real string literals count, never comments ──────────────────────────────

    [Theory]
    [InlineData("var a = \"first-name\";", "first-name")]
    [InlineData("var a = @\"first-name\";", "first-name")]
    [InlineData("var a = \"\"\"first-name\"\"\";", "first-name")]
    [InlineData("var a = $\"{(x ? \"first-name\" : \"\")}\";", "first-name")]
    [InlineData("var a = \"say \\\"hi\\\"\";", "say \"hi\"")]
    public void CSharpLiterals_FindsTheLiteral(string source, string expected)
    {
        Assert.Contains(expected, CSharpLexer.Literals(source));
    }

    [Theory]
    [InlineData("// \"first-name\"")]
    [InlineData("/// <c>\"first-name\"</c>")]
    [InlineData("/* \"first-name\" */")]
    [InlineData("var c = '\"'; // \"first-name\"")]
    [InlineData("var a = $\"q-{name}\";")]
    public void CSharpLiterals_IgnoresCommentsAndInterpolatedText(string source)
    {
        Assert.DoesNotContain("first-name", CSharpLexer.Literals(source));
        Assert.DoesNotContain("q-", CSharpLexer.Literals(source));
    }

    [Fact]
    public void RazorLiterals_IgnoresRazorAndHtmlComments()
    {
        var literals = RazorLiterals("@* \"first-name\" *@ <!-- \"last-name\" --> Page = new() { Id = \"upn\" }");

        Assert.Equal(["upn"], literals);
    }

    // ── Scan ────────────────────────────────────────────────────────────────────────────────────

    private static readonly Lazy<HashSet<string>> FlowIds = new(() =>
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(
                     Path.Combine(RepoRoot(), "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows"), "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var page in doc.RootElement.GetProperty("pages").EnumerateArray())
            {
                ids.Add(page.GetProperty("id").GetString()!);
                if (!page.TryGetProperty("questions", out var questions)
                    || questions.ValueKind != JsonValueKind.Array) continue;
                foreach (var question in questions.EnumerateArray())
                    ids.Add(question.GetProperty("id").GetString()!);
            }
        }
        return ids;
    });

    /// <summary>Each repo-relative source path (forward slashes) and the flow ids it names.</summary>
    private static readonly Lazy<Dictionary<string, HashSet<string>>> Scan = new(() =>
    {
        var root = RepoRoot();
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var path in SourceFiles())
        {
            var text = File.ReadAllText(path);
            var literals = path.EndsWith(".cshtml", StringComparison.Ordinal)
                ? RazorLiterals(text)
                : CSharpLexer.Literals(text);
            var named = literals.Where(FlowIds.Value.Contains).ToHashSet(StringComparer.Ordinal);
            if (named.Count > 0)
                result[Path.GetRelativePath(root, path).Replace('\\', '/')] = named;
        }
        return result;
    });

    private static IEnumerable<string> SourceFiles()
    {
        var src = Path.Combine(RepoRoot(), "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(src, "*.cshtml", SearchOption.AllDirectories))
            .Where(p =>
            {
                var segments = Path.GetRelativePath(src, p).Split(Path.DirectorySeparatorChar);
                return !segments.Contains("bin") && !segments.Contains("obj") && !segments.Contains("Migrations");
            });
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => path;

    // Repo root is three levels up from tests/DfE.CheckPerformanceData.UnitTests/Journey/.
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", ".."));

    private static string Describe(IEnumerable<(string File, string Id)> entries) =>
        string.Join("\n", entries
            .GroupBy(e => e.File)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"  {g.Key}: {string.Join(", ", g.Select(e => e.Id).Order(StringComparer.Ordinal))}"));

    private static string BaselineLines(IEnumerable<(string File, string Id)> entries) =>
        string.Join("\n", entries
            .GroupBy(e => e.File)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"        [\"{g.Key}\"] = [{string.Join(", ", g.Select(e => $"\"{e.Id}\"").Order(StringComparer.Ordinal))}],"));

    // ── Literal extraction ──────────────────────────────────────────────────────────────────────

    private static readonly Regex RazorComment = new(@"@\*.*?\*@|<!--.*?-->", RegexOptions.Singleline);
    private static readonly Regex RazorQuoted = new(@"[""']([A-Za-z0-9_\-]+)[""']");

    // A view mixes C#, HTML and prose (with apostrophes), so a lexer does not fit. Only a quoted
    // word with no spaces can equal an id, so a regex over the text without comments is enough.
    private static List<string> RazorLiterals(string text) =>
        RazorQuoted.Matches(RazorComment.Replace(text, ""))
            .Select(m => m.Groups[1].Value)
            .ToList();

    /// <summary>
    /// Finds the string literals in C# source: regular, verbatim, raw and interpolated strings,
    /// including strings inside interpolation holes. Comments and char literals are skipped. The
    /// text of an interpolated string has a marker in each hole, so <c>$"q-{x}"</c> never
    /// equals an id.
    /// </summary>
    private sealed class CSharpLexer
    {
        private readonly string _s;
        private readonly List<string> _literals = [];
        private int _i;

        private CSharpLexer(string s) => _s = s;

        public static List<string> Literals(string source)
        {
            var lexer = new CSharpLexer(source);
            while (lexer._i < source.Length) lexer.Step();
            return lexer._literals;
        }

        private char At(int offset) => _i + offset < _s.Length ? _s[_i + offset] : '\0';

        private void Step()
        {
            var c = At(0);
            if (c == '/' && At(1) == '/')
            {
                var end = _s.IndexOf('\n', _i);
                _i = end < 0 ? _s.Length : end;
            }
            else if (c == '/' && At(1) == '*')
            {
                var end = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                _i = end < 0 ? _s.Length : end + 2;
            }
            else if (c == '\'')
            {
                SkipCharLiteral();
            }
            else if (StartsString())
            {
                ReadString();
            }
            else
            {
                _i++;
            }
        }

        private bool StartsString()
        {
            var j = _i;
            while (j < _s.Length && (_s[j] == '$' || _s[j] == '@')) j++;
            return j < _s.Length && _s[j] == '"';
        }

        private void SkipCharLiteral()
        {
            _i++;
            if (At(0) == '\\') _i++;
            _i++;
            while (_i < _s.Length && _s[_i] != '\'') _i++;
            _i++;
        }

        private void ReadString()
        {
            var interpolated = false;
            var verbatim = false;
            while (At(0) is '$' or '@')
            {
                if (At(0) == '$') interpolated = true; else verbatim = true;
                _i++;
            }

            var quotes = 0;
            while (At(quotes) == '"') quotes++;

            if (quotes >= 3)
            {
                // Raw string: ends at the same number of quotes. Holes in an interpolated raw string
                // are rare and hold no flow ids, so the text is taken as it is.
                var start = _i + quotes;
                var end = _s.IndexOf(new string('"', quotes), start, StringComparison.Ordinal);
                if (end < 0) end = _s.Length;
                _literals.Add(_s[start..end].Trim());
                _i = Math.Min(_s.Length, end + quotes);
                return;
            }

            _i++; // opening quote
            var text = new StringBuilder();
            while (_i < _s.Length)
            {
                var c = At(0);
                if (verbatim && c == '"' && At(1) == '"') { text.Append('"'); _i += 2; }
                else if (c == '"') { _i++; break; }
                else if (!verbatim && c == '\\') { text.Append(At(1)); _i += 2; }
                else if (!verbatim && c == '\n') break;
                else if (interpolated && c == '{' && At(1) == '{') { text.Append('{'); _i += 2; }
                else if (interpolated && c == '}' && At(1) == '}') { text.Append('}'); _i += 2; }
                else if (interpolated && c == '{') { text.Append('\0'); ReadHole(); }
                else { text.Append(c); _i++; }
            }
            _literals.Add(text.ToString());
        }

        // An interpolation hole is C#, so it can hold strings of its own.
        private void ReadHole()
        {
            _i++; // {
            var depth = 1;
            while (_i < _s.Length && depth > 0)
            {
                var c = At(0);
                if (c == '\'') SkipCharLiteral();
                else if (StartsString()) ReadString();
                else
                {
                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                    _i++;
                }
            }
        }
    }
}
