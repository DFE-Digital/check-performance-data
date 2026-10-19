using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Both runtime images must run as the aspnet image's unprivileged `app` user, and the AKS modules
// must enforce it (#609, OWASP least privilege). The user is written as `USER $APP_UID` — uid 1654,
// numeric — because Kubernetes' runAsNonRoot check reads the image's USER and refuses a name.
// Locally nothing notices a dropped line: the image builds and compose runs it as root. On AKS the
// pod is refused at deploy time, far from the cause.
public sealed class ContainerUserSourceTests
{
    [Theory]
    [InlineData("DfE.CheckPerformanceData.Web")]
    [InlineData("DfE.CheckPerformanceData.RulesEngineWorker")]
    public void BaseStage_DeclaresTheAppUser(string project)
    {
        var dockerfile = File.ReadAllText(DockerfilePath(project));

        Assert.Contains("$APP_UID", UserDirectives(StageNamed(dockerfile, "base")));
    }

    // The effective user is the last USER directive across the stages the shipped one builds on, so a
    // later `USER root` in the shipped stage (or in base) must fail even though base still names the app user.
    [Theory]
    [InlineData("DfE.CheckPerformanceData.Web", "runtime")]
    [InlineData("DfE.CheckPerformanceData.RulesEngineWorker", "final")]
    public void ShippedStage_RunsAsTheAppUser(string project, string shippedStage)
    {
        var dockerfile = File.ReadAllText(DockerfilePath(project));
        var shipped = StageNamed(dockerfile, shippedStage);

        Assert.StartsWith("FROM base AS", shipped);

        var users = UserDirectives(StageNamed(dockerfile, "base")).Concat(UserDirectives(shipped)).ToList();

        Assert.NotEmpty(users);
        Assert.Equal("$APP_UID", users[^1]);
    }

    [Fact]
    public void WebDebugStage_HandsBackToTheAppUser_AfterInstallingTheDebugger()
    {
        var dockerfile = File.ReadAllText(DockerfilePath("DfE.CheckPerformanceData.Web"));
        var stage = StageNamed(dockerfile, "debug-runtime");

        var users = UserMatches(stage);
        var install = stage.IndexOf("apt-get install", StringComparison.Ordinal);
        var root = users.FirstOrDefault(m => m.Groups[1].Value == "root");
        var app = users.LastOrDefault(m => m.Groups[1].Value == "$APP_UID");

        Assert.True(install >= 0, "the debug stage installs the debugger with apt-get");
        Assert.True(root is not null && root.Index < install, "the apt-get install needs root, so USER root must come before it");
        Assert.True(app is not null && app.Index > install, "the stage must hand back to the app user after the install");
    }

    [Theory]
    [InlineData("web_application")]
    [InlineData("rules_engine_worker")]
    public void AksModule_EnforcesNonRoot(string module)
    {
        var tf = File.ReadAllText(Path.Combine(RepoRoot, "terraform", "application", "application.tf"));

        Assert.Matches(new Regex(@"^[ \t]*run_as_non_root[ \t]*=[ \t]*true\b", RegexOptions.Multiline), ModuleBlock(tf, module));
    }

    // The text of one build stage: its `FROM … AS <name>` line through to the next `FROM` or the end.
    private static string StageNamed(string dockerfile, string name)
    {
        var match = Regex.Match(
            dockerfile,
            $@"^FROM [^\r\n]* AS {Regex.Escape(name)}[ \t]*\r?$.*?(?=^FROM |\z)",
            RegexOptions.Multiline | RegexOptions.Singleline);

        Assert.True(match.Success, $"no stage named {name}");
        return match.Value;
    }

    // Real USER directives only (anchored at line start), so a comment naming one never counts.
    private static MatchCollection UserMatches(string stageText) =>
        Regex.Matches(stageText, @"^[ \t]*USER[ \t]+(\S+)[ \t]*\r?$", RegexOptions.Multiline);

    private static List<string> UserDirectives(string stageText) =>
        UserMatches(stageText).Select(m => m.Groups[1].Value).ToList();

    private static string DockerfilePath(string project) => Path.Combine(RepoRoot, "src", project, "Dockerfile");

    // The text of one `module "<name>" { … }` block, up to its closing brace at column 0.
    private static string ModuleBlock(string tf, string name)
    {
        var start = tf.IndexOf($"module \"{name}\" {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no module {name}");

        var end = tf.IndexOf("\n}", start, StringComparison.Ordinal);
        Assert.True(end > start, $"module {name} never closes");
        return tf[start..end];
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(ThisFilePath())!);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Makefile")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName
                ?? throw new InvalidOperationException("Could not locate the repository root from the test file path.");
        }
    }

    private static string ThisFilePath([CallerFilePath] string path = "") => path;
}
