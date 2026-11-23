using AngleSharp.Html.Parser;
using DfE.CheckPerformanceData.Application.Media.RichText;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.RichText;

// The image dialog in the rich text editor always saves a width and a height. The box marker says
// whether the editor gave the image its own shape on purpose ("fixed"), or kept the shape of the
// file ("follow"), so a new file of another shape is shown in the right box.
public sealed class RichTextImageBoxTests
{
    [Theory]
    [InlineData(400, 200, 800, 400)]
    [InlineData(400, 201, 800, 400)] // the dialog rounds the side it works out
    [InlineData(401, 200, 800, 400)]
    [InlineData(21, 10, 1280, 640)]
    [InlineData(1280, 640, 1280, 640)]
    public void ABoxWithinAPixelOfTheFilesShape_HasItsShape(int width, int height, int fileWidth, int fileHeight)
    {
        Assert.True(RichTextImageBox.HasShapeOf(width, height, fileWidth, fileHeight));
        Assert.Equal(RichTextImageBox.Follow, RichTextImageBox.For(width, height, fileWidth, fileHeight));
    }

    [Theory]
    [InlineData(320, 320, 1280, 640)]
    [InlineData(400, 203, 800, 400)]
    [InlineData(400, 200, 600, 600)]
    [InlineData(20, 12, 20, 10)] // two pixels on a small image is a shape the editor chose
    public void ABoxOfAnotherShape_IsFixed(int width, int height, int fileWidth, int fileHeight)
    {
        Assert.False(RichTextImageBox.HasShapeOf(width, height, fileWidth, fileHeight));
        Assert.Equal(RichTextImageBox.Fixed, RichTextImageBox.For(width, height, fileWidth, fileHeight));
    }

    [Theory]
    [InlineData("fixed", RichTextImageBox.Fixed)]
    [InlineData(" Follow ", RichTextImageBox.Follow)]
    [InlineData("stretch", null)]
    [InlineData("", null)]
    public void Read_KnowsOnlyTheTwoValues(string value, string? expected)
    {
        var img = new HtmlParser().ParseDocument($"<img data-media-box=\"{value}\">").QuerySelector("img")!;

        Assert.Equal(expected, RichTextImageBox.Read(img));
    }

    [Fact]
    public void Read_IsNull_WithNoMarker()
    {
        var img = new HtmlParser().ParseDocument("<img width=\"4\" height=\"2\">").QuerySelector("img")!;

        Assert.Null(RichTextImageBox.Read(img));
    }
}
