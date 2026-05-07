using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DfE.CheckPerformanceData.E2ETests.Helpers;

public sealed class PageSnapshotExtensionsTests
{
    // --- Single-pixel divergence ---

    [Fact]
    public async Task BuildDiffArtefacts_WritesThreePngs_WhenExpectedAndActualDiffer()
    {
        var tempDir = CreateTempDir();
        try
        {
            using var expected = MakeUniformImage(4, 4, new Rgba32(255, 255, 255, 255));
            using var actual = MakeUniformImage(4, 4, new Rgba32(255, 255, 255, 255));
            actual[0, 0] = new Rgba32(0, 0, 0, 255);

            await PageSnapshotExtensions.BuildDiffArtefactsAsync(
                expected: expected,
                actual: actual,
                outputDir: tempDir,
                stem: "single-corner-diff",
                perChannelTolerance: 3);

            Assert.True(File.Exists(Path.Combine(tempDir, "single-corner-diff.expected.png")));
            Assert.True(File.Exists(Path.Combine(tempDir, "single-corner-diff.actual.png")));
            Assert.True(File.Exists(Path.Combine(tempDir, "single-corner-diff.diff.png")));

            using var diff = await Image.LoadAsync<Rgba32>(
                Path.Combine(tempDir, "single-corner-diff.diff.png"));
            Assert.Equal(4, diff.Width);
            Assert.Equal(4, diff.Height);
            Assert.Equal(new Rgba32(255, 0, 0, 255), diff[0, 0]);
            // Non-diverging pixel: should equal actual (not tinted)
            Assert.Equal(new Rgba32(255, 255, 255, 255), diff[3, 3]);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // --- Multi-pixel divergence ---

    [Fact]
    public async Task BuildDiffArtefacts_TintsAllDifferingPixels_NotJustOne()
    {
        var tempDir = CreateTempDir();
        try
        {
            using var expected = MakeUniformImage(2, 2, new Rgba32(255, 255, 255, 255));
            using var actual = MakeUniformImage(2, 2, new Rgba32(255, 255, 255, 255));
            // Bottom row entirely diverged
            actual[0, 1] = new Rgba32(0, 0, 0, 255);
            actual[1, 1] = new Rgba32(0, 0, 0, 255);

            await PageSnapshotExtensions.BuildDiffArtefactsAsync(
                expected, actual, tempDir, "multi-pixel-diff", 3);

            using var diff = await Image.LoadAsync<Rgba32>(
                Path.Combine(tempDir, "multi-pixel-diff.diff.png"));
            // Top row: untouched (= actual)
            Assert.Equal(new Rgba32(255, 255, 255, 255), diff[0, 0]);
            Assert.Equal(new Rgba32(255, 255, 255, 255), diff[1, 0]);
            // Bottom row: tinted red
            Assert.Equal(new Rgba32(255, 0, 0, 255), diff[0, 1]);
            Assert.Equal(new Rgba32(255, 0, 0, 255), diff[1, 1]);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // --- Identical inputs (helper still emits, tinting nothing) ---

    [Fact]
    public async Task BuildDiffArtefacts_LeavesIdenticalPixelsUntinted_WhenWithinTolerance()
    {
        var tempDir = CreateTempDir();
        try
        {
            using var expected = MakeUniformImage(2, 2, new Rgba32(123, 45, 67, 255));
            using var actual = MakeUniformImage(2, 2, new Rgba32(123, 45, 67, 255));

            await PageSnapshotExtensions.BuildDiffArtefactsAsync(
                expected, actual, tempDir, "identical", 3);

            Assert.True(File.Exists(Path.Combine(tempDir, "identical.diff.png")));

            using var diff = await Image.LoadAsync<Rgba32>(
                Path.Combine(tempDir, "identical.diff.png"));
            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 2; x++)
                    Assert.Equal(new Rgba32(123, 45, 67, 255), diff[x, y]);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // --- Tolerance gate ---

    [Fact]
    public async Task BuildDiffArtefacts_RespectsPerChannelTolerance()
    {
        var tempDir = CreateTempDir();
        try
        {
            using var expected = MakeUniformImage(2, 1, new Rgba32(100, 100, 100, 255));
            using var actual = MakeUniformImage(2, 1, new Rgba32(100, 100, 100, 255));
            // Pixel 0: delta = 2 <= tolerance 3 -> not tinted
            actual[0, 0] = new Rgba32(102, 100, 100, 255);
            // Pixel 1: delta = 4 > tolerance 3 -> tinted
            actual[1, 0] = new Rgba32(104, 100, 100, 255);

            await PageSnapshotExtensions.BuildDiffArtefactsAsync(
                expected, actual, tempDir, "tolerance", perChannelTolerance: 3);

            using var diff = await Image.LoadAsync<Rgba32>(
                Path.Combine(tempDir, "tolerance.diff.png"));
            Assert.Equal(new Rgba32(102, 100, 100, 255), diff[0, 0]); // unchanged
            Assert.Equal(new Rgba32(255, 0, 0, 255), diff[1, 0]);     // tinted
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // --- Helpers ---

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cpd-snapshot-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Image<Rgba32> MakeUniformImage(int width, int height, Rgba32 colour)
    {
        var img = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                img[x, y] = colour;
        return img;
    }
}
