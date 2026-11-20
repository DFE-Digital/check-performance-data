using DfE.CheckPerformanceData.Application.Media;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media;

public sealed class MediaBlobKeysTests
{
    private static readonly Guid Id = new("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Fact]
    public void Original_IsUnderOriginals_ByImageAndVersion()
    {
        Assert.Equal("originals/0f8fad5b-d9cb-469f-a165-70867728950e/3", MediaBlobKeys.Original(Id, 3));
    }

    [Fact]
    public void Rendition_IsUnderItsVersionsPrefix()
    {
        var key = MediaBlobKeys.Rendition(Id, 2, "a1b2c3");

        Assert.Equal("renditions/0f8fad5b-d9cb-469f-a165-70867728950e/2/a1b2c3", key);
        Assert.StartsWith(MediaBlobKeys.RenditionsPrefix(Id, 2), key);
    }

    // The hash ends up in a blob name, so it must not be able to climb out of its folder.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../1/x")]
    [InlineData("ab/cd")]
    [InlineData("ab.cd")]
    public void Rendition_RefusesAHashThatIsNotLettersAndDigits(string hash)
    {
        Assert.ThrowsAny<ArgumentException>(() => MediaBlobKeys.Rendition(Id, 1, hash));
    }
}
