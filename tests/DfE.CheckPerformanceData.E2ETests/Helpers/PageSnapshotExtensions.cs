using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using Xunit.Sdk;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public static class PageSnapshotExtensions
{
    private const string SnapshotsRootRelative = "Snapshots/linux-chromium";
    private const string SnapshotsDiffsRelative = "diffs";
    private const string TestProjectMarker = "DfE.CheckPerformanceData.E2ETests.csproj";
    private const int PerChannelTolerance = 3;

    public static async Task MatchSnapshotAsync(
        this IPage page,
        string name,
        double maxDiffPixelRatio = 0.005,
        bool fullPage = true,
        ICollection<string>? createdSnapshots = null)
    {
        var actualBytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = fullPage,
            Type = ScreenshotType.Png
        });

        var snapshotPath = ResolveSnapshotPath(name);

        if (!File.Exists(snapshotPath))
        {
            // Two-pass bootstrap. Snapshots aren't committed to the repo — each environment
            // (CI Linux, dev macOS, dev Windows) keeps its own canonical PNG because cross-OS
            // Chromium produces slightly different output. First run on a clean environment
            // writes the baseline and returns success; the next run compares against it.
            // The optional accumulator lets callers report all bootstrapped names from a
            // multi-viewport sweep at once instead of revealing one per test run.
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllBytesAsync(snapshotPath, actualBytes);

            createdSnapshots?.Add(name);
            return;
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

        expected.ProcessPixelRows(actual, (eAccessor, aAccessor) =>
        {
            long count = 0;
            for (var y = 0; y < eAccessor.Height; y++)
            {
                var eRow = eAccessor.GetRowSpan(y);
                var aRow = aAccessor.GetRowSpan(y);

                for (var x = 0; x < eRow.Length; x++)
                {
                    var e = eRow[x];
                    var a = aRow[x];

                    if (Math.Abs(e.R - a.R) > PerChannelTolerance
                        || Math.Abs(e.G - a.G) > PerChannelTolerance
                        || Math.Abs(e.B - a.B) > PerChannelTolerance
                        || Math.Abs(e.A - a.A) > PerChannelTolerance)
                    {
                        count++;
                    }
                }
            }
            differingPixels = count;
        });

        var ratio = (double)differingPixels / totalPixels;
        if (ratio > maxDiffPixelRatio)
        {
            var diffsDir = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(snapshotPath)!, "..", SnapshotsDiffsRelative));
            var stem = Path.GetFileNameWithoutExtension(name);

            await BuildDiffArtefactsAsync(
                expected, actual, diffsDir, stem, PerChannelTolerance);

            throw new XunitException(
                $"Snapshot {name} diverged by {ratio:P3} ({differingPixels} of {totalPixels} pixels) "
                + $"which exceeds threshold {maxDiffPixelRatio:P3}. "
                + $"Diff PNGs written to {diffsDir}.");
        }
    }

    public static async Task BuildDiffArtefactsAsync(
        Image<Rgba32> expected,
        Image<Rgba32> actual,
        string outputDir,
        string stem,
        int perChannelTolerance)
    {
        Directory.CreateDirectory(outputDir);

        var expectedOut = Path.Combine(outputDir, $"{stem}.expected.png");
        var actualOut   = Path.Combine(outputDir, $"{stem}.actual.png");
        var diffOut     = Path.Combine(outputDir, $"{stem}.diff.png");

        await expected.SaveAsPngAsync(expectedOut);
        await actual.SaveAsPngAsync(actualOut);

        using var diff = BuildDiffImage(expected, actual, perChannelTolerance);
        await diff.SaveAsPngAsync(diffOut);
    }

    private static Image<Rgba32> BuildDiffImage(
        Image<Rgba32> expected,
        Image<Rgba32> actual,
        int perChannelTolerance)
    {
        var diff = actual.Clone();
        expected.ProcessPixelRows(actual, diff, (eAccessor, aAccessor, dAccessor) =>
        {
            for (var y = 0; y < eAccessor.Height; y++)
            {
                var eRow = eAccessor.GetRowSpan(y);
                var aRow = aAccessor.GetRowSpan(y);
                var dRow = dAccessor.GetRowSpan(y);

                for (var x = 0; x < eRow.Length; x++)
                {
                    var e = eRow[x];
                    var a = aRow[x];

                    if (Math.Abs(e.R - a.R) > perChannelTolerance
                        || Math.Abs(e.G - a.G) > perChannelTolerance
                        || Math.Abs(e.B - a.B) > perChannelTolerance
                        || Math.Abs(e.A - a.A) > perChannelTolerance)
                    {
                        dRow[x] = new Rgba32(255, 0, 0, 255);
                    }
                }
            }
        });
        return diff;
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
