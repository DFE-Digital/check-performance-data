using System.Runtime.CompilerServices;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

public sealed class RulesEngineWorkerProgramTests
{
    [Fact]
    public void Program_DoesNotWriteToStdout()
    {
        var src = ReadCompositionSource();

        Assert.DoesNotContain("Console.Write", src);
    }

    [Fact]
    public void Program_GatesDevZendeskFakeOnConfigFlagNotEnvironmentName()
    {
        var src = ReadCompositionSource();

        // The dev outbox fake is selected through the Zendesk:UseFake config flag, not the
        // environment name, so the test site (which runs as Development) can be flipped to the
        // real Zendesk client by config alone.
        Assert.Contains("ConfigureFakeZendesk", src);
        Assert.DoesNotContain("IsDevelopment()", src);
    }

    // The worker's composition is Program.cs plus the service graph it hands off to
    // WorkerServiceExtensions, so both are read: a check on Program.cs alone would pass vacuously
    // for anything that moved into the extension.
    private static string ReadCompositionSource([CallerFilePath] string testFile = "")
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

        var worker = Path.Combine(dir.FullName, "src", "DfE.CheckPerformanceData.RulesEngineWorker");
        return File.ReadAllText(Path.Combine(worker, "Program.cs"))
            + File.ReadAllText(Path.Combine(worker, "WorkerServiceExtensions.cs"));
    }
}
