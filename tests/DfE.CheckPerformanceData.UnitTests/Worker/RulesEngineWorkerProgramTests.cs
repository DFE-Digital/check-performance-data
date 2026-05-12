using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

public sealed class RulesEngineWorkerProgramTests
{
    [Fact]
    public void Program_DoesNotWriteToStdout()
    {
        var src = File.ReadAllText(ResolveProgramPath());

        Assert.DoesNotContain("Console.Write", src);
    }

    private static string ResolveProgramPath([CallerFilePath] string testFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(testFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Makefile")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate repository root from test file path.");
        }

        return Path.Combine(
            dir.FullName,
            "src",
            "DfE.CheckPerformanceData.RulesEngineWorker",
            "Program.cs");
    }
}
