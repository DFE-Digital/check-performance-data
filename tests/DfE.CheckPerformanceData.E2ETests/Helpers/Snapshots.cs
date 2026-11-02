namespace DfE.CheckPerformanceData.E2ETests.Helpers;

// Finds the folder where tests save a screenshot when they fail. The folder is
// Snapshots/failures in the source tree, not under bin/, so CI can upload it.
public static class Snapshots
{
    private const string FailuresDirRelative = "Snapshots/failures";
    private const string TestProjectMarker = "DfE.CheckPerformanceData.E2ETests.csproj";

    public static string FailuresDir
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null)
            {
                var marker = Path.Combine(current.FullName, TestProjectMarker);
                if (File.Exists(marker))
                {
                    var dir = Path.Combine(current.FullName, FailuresDirRelative);
                    Directory.CreateDirectory(dir);
                    return dir;
                }
                current = current.Parent;
            }

            throw new InvalidOperationException(
                $"Could not locate {TestProjectMarker} ancestor of {AppContext.BaseDirectory}.");
        }
    }
}
