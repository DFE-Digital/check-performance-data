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

    // Deleting an image removes every file under these two prefixes: all its originals and all
    // its renditions. The trailing slash stops one id matching another that starts the same way.
    [Fact]
    public void AssetPrefixes_CoverEveryOriginalAndRenditionOfTheImage_AndNothingElse()
    {
        var prefixes = MediaBlobKeys.AssetPrefixes(Id);

        Assert.Equal(
            ["originals/0f8fad5b-d9cb-469f-a165-70867728950e/", "renditions/0f8fad5b-d9cb-469f-a165-70867728950e/"],
            prefixes);
        Assert.Contains(prefixes, p => MediaBlobKeys.Original(Id, 7).StartsWith(p, StringComparison.Ordinal));
        Assert.Contains(prefixes, p => MediaBlobKeys.Rendition(Id, 7, "abc").StartsWith(p, StringComparison.Ordinal));
        var other = Guid.NewGuid();
        Assert.DoesNotContain(prefixes, p => MediaBlobKeys.Original(other, 1).StartsWith(p, StringComparison.Ordinal));
    }

    // The size report adds up the resized copies of each image from one listing of the container.
    [Fact]
    public void EveryRendition_IsUnderTheRenditionsPrefix_AndNamesItsImage()
    {
        var key = MediaBlobKeys.Rendition(Id, 3, "abc");

        Assert.StartsWith(MediaBlobKeys.AllRenditionsPrefix, key, StringComparison.Ordinal);
        Assert.True(MediaBlobKeys.TryGetRenditionAsset(key, out var asset));
        Assert.Equal(Id, asset);
    }

    [Theory]
    [InlineData("originals/0f8fad5b-d9cb-469f-a165-70867728950e/1")]
    [InlineData("renditions/not-a-guid/1/abc")]
    [InlineData("renditions/")]
    [InlineData("")]
    public void AKeyThatIsNotARendition_NamesNoImage(string key)
    {
        Assert.False(MediaBlobKeys.TryGetRenditionAsset(key, out _));
    }
}
