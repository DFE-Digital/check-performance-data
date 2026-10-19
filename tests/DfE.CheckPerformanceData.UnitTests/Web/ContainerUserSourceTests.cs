using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Both runtime images must run as the aspnet image's unprivileged `app` user, and the AKS modules
// must enforce it (#609, OWASP least privilege). The user is written as `USER $APP_UID` — uid 1654,
// numeric — because Kubernetes' runAsNonRoot check reads the image's USER and refuses a name.
// Nothing else in the build notices a dropped line: the image still builds and the pod still
// starts, and the only difference is that `id` inside it says root.
public sealed class ContainerUserSourceTests
{
    [Theory]
    [InlineData("DfE.CheckPerformanceData.Web")]
    [InlineData("DfE.CheckPerformanceData.RulesEngineWorker")]
    public void BaseStage_RunsAsTheAppUser(string project)
    {
        var dockerfile = File.ReadAllText(Path.Combine(RepoRoot, "src", project, "Dockerfile"));

        Assert.Contains("USER $APP_UID", StageNamed(dockerfile, "base"));
    }

    [Fact]
    public void WebDebugStage_HandsBackToTheAppUser_AfterInstallingTheDebugger()
    {
        var dockerfile = File.ReadAllText(Path.Combine(RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Dockerfile"));
        var stage = StageNamed(dockerfile, "debug-runtime");

        var root = stage.IndexOf("USER root", StringComparison.Ordinal);
        var install = stage.IndexOf("apt-get install", StringComparison.Ordinal);
        var app = stage.LastIndexOf("USER $APP_UID", StringComparison.Ordinal);

        Assert.True(install >= 0, "the debug stage installs the debugger with apt-get");
        Assert.True(root >= 0 && root < install, "the apt-get install needs root, so USER root must come before it");
        Assert.True(app > install, "the stage must hand back to the app user after the install");
    }

    [Theory]
    [InlineData("web_application")]
    [InlineData("rules_engine_worker")]
    public void AksModule_EnforcesNonRoot(string module)
    {
        var tf = File.ReadAllText(Path.Combine(RepoRoot, "terraform", "application", "application.tf"));

        Assert.Matches(new Regex(@"run_as_non_root\s*=\s*true"), ModuleBlock(tf, module));
    }

    // The text of one build stage: from its `FROM … AS <name>` line to the next `FROM` or the end.
    private static string StageNamed(string dockerfile, string name)
    {
        var match = Regex.Match(
            dockerfile,
            $@"^FROM .* AS {Regex.Escape(name)}[ \t]*\r?$(.*?)(?=^FROM |\z)",
            RegexOptions.Multiline | RegexOptions.Singleline);

        Assert.True(match.Success, $"no stage named {name}");
        return match.Groups[1].Value;
    }

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
