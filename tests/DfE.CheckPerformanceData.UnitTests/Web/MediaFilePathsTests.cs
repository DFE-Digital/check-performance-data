using DfE.CheckPerformanceData.Web.Middleware;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Only the media file route skips the session; every other path keeps it, because it may reach
// the content page route.
public sealed class MediaFilePathsTests
{
    [Theory]
    [InlineData("/media/8a8e3a52-38a9-4a65-9e3a-5f8f2f0f7a11/1/photo.png", true)]
    [InlineData("/MEDIA/x/1/photo.webp", true)]
    [InlineData("/media", false)]
    [InlineData("/media/", false)]
    [InlineData("/media/x", false)]
    [InlineData("/media/x/1", false)]
    [InlineData("/media/x/1/", false)]
    [InlineData("/media/x/1/a.png/extra", false)]
    [InlineData("/media//1/a.png", false)]
    [InlineData("/mediaplayer/x/1/a.png", false)]
    [InlineData("/help/media/x/1/a.png", false)]
    public void MatchesOnlyTheMediaFileRoute(string path, bool expected)
    {
        Assert.Equal(expected, MediaFilePaths.IsMediaFile(new PathString(path)));
    }
}
