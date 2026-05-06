using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit.Sdk;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class PageSnapshotExtensions
{
    private const string SnapshotsRootRelative = "Snapshots/linux-chromium";
    private const string TestProjectMarker = "DfE.CheckPerformanceData.E2ETests.csproj";
    private const int PerChannelTolerance = 3;

    public static async Task MatchSnapshotAsync(
        this IPage page,
        string name,
        double maxDiffPixelRatio = 0.005,
        bool fullPage = true)
    {
        var actualBytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = fullPage,
            Type = ScreenshotType.Png
        });

        var snapshotPath = ResolveSnapshotPath(name);

        if (!File.Exists(snapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllBytesAsync(snapshotPath, actualBytes);
            throw new XunitException(
                $"Snapshot {name} did not exist at {snapshotPath} — written, run again to verify.");
        }

        using var expected = Image.Load<Rgba32>(snapshotPath);
        using var actualStream = new MemoryStream(actualBytes);
        using var actual = Image.Load<Rgba32>(actualStream);

        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            throw new XunitException(
                $"Snapshot {name} dimensions differ: expected {expected.Width}x{expected.Height}, "
                + $"actual {actual.Width}x{actual.Height}.");
        }

        var totalPixels = (long)expected.Width * expected.Height;
        long differingPixels = 0;

        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var e = expected[x, y];
                var a = actual[x, y];

                if (Math.Abs(e.R - a.R) > PerChannelTolerance
                    || Math.Abs(e.G - a.G) > PerChannelTolerance
                    || Math.Abs(e.B - a.B) > PerChannelTolerance
                    || Math.Abs(e.A - a.A) > PerChannelTolerance)
                {
                    differingPixels++;
                }
            }
        }

        var ratio = (double)differingPixels / totalPixels;
        if (ratio > maxDiffPixelRatio)
        {
            throw new XunitException(
                $"Snapshot {name} diverged by {ratio:P3} ({differingPixels} of {totalPixels} pixels) "
                + $"which exceeds threshold {maxDiffPixelRatio:P3}.");
        }
    }

    private static string ResolveSnapshotPath(string name)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, TestProjectMarker);
            if (File.Exists(marker))
            {
                return Path.Combine(current.FullName, SnapshotsRootRelative, name);
            }
            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {TestProjectMarker} ancestor of {AppContext.BaseDirectory}.");
    }
}
