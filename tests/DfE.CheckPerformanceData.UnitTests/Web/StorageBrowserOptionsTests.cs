using DfE.CheckPerformanceData.Web.Admin;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class StorageBrowserOptionsTests
{
    // #568: 8 MiB parts; 2 GiB ceiling (the product owner's number).
    [Fact]
    public void Defaults_Are8MiBParts_2GiBMaximum()
    {
        var options = new StorageBrowserOptions();

        Assert.Equal(8 * 1024 * 1024, options.ChunkBytes);
        Assert.Equal(2L * 1024 * 1024 * 1024, options.MaxUploadBytes);
        Assert.Equal(256, options.MaxBlocks);
    }

    // The stage action's request ceiling is a compile-time attribute value, so a configured
    // part size has to fit under it or every part would be refused.
    [Fact]
    public void ConfiguredChunk_MustFitUnderTheRequestCeiling()
    {
        Assert.True(new StorageBrowserOptions().ChunkBytes <= StorageBrowserOptions.ChunkRequestCeilingBytes);
        Assert.Equal(32L * 1024 * 1024, StorageBrowserOptions.ChunkRequestCeilingBytes);
    }

    [Fact]
    public void MaxBlocks_RoundsUp()
    {
        var options = new StorageBrowserOptions { ChunkBytes = 10, MaxUploadBytes = 25 };
        Assert.Equal(3, options.MaxBlocks);
    }

    [Theory]
    [InlineData(2L * 1024 * 1024 * 1024, "2GB")]
    [InlineData(1024L * 1024 * 1024, "1GB")]
    [InlineData(1536L * 1024 * 1024, "1.5GB")]
    [InlineData(500L * 1024 * 1024, "500MB")]
    [InlineData(8L * 1024 * 1024, "8MB")]
    [InlineData(50L * 1024 * 1024, "50MB")]
    public void FormatSize_UsesGovUkStyleWholeUnits(long bytes, string expected) =>
        Assert.Equal(expected, StorageBrowserOptions.FormatSize(bytes));
}
