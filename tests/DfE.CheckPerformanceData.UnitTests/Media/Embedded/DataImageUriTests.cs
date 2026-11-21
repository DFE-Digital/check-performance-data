using System.Text;
using DfE.CheckPerformanceData.Application.Media.Embedded;
using DfE.CheckPerformanceData.Application.Media.Upload;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Embedded;

// Older rich text holds images as data: URIs. Each one is decoded to the bytes of a file, or a
// reason in plain English why it cannot move to the media library. Nothing here throws.
public sealed class DataImageUriTests
{
    internal const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    [Fact]
    public void ABase64Png_GivesItsBytesAndFormat()
    {
        var decoded = DataImageUri.Decode($"data:image/png;base64,{OnePixelPng}");

        Assert.Null(decoded.Problem);
        Assert.Equal(MediaFormat.Png, decoded.Format);
        Assert.Equal(Convert.FromBase64String(OnePixelPng), decoded.Bytes);
    }

    [Fact]
    public void TheFormatComesFromTheBytes_NotTheDeclaredType()
    {
        var decoded = DataImageUri.Decode($"data:image/jpeg;base64,{OnePixelPng}");

        Assert.Equal(MediaFormat.Png, decoded.Format);
    }

    [Fact]
    public void Base64WithLineBreaksAndSpaces_IsRead()
    {
        var wrapped = OnePixelPng[..20] + "\n " + OnePixelPng[20..40] + "\r\n" + OnePixelPng[40..];

        var decoded = DataImageUri.Decode($" data:image/png;base64,{wrapped} ");

        Assert.Null(decoded.Problem);
        Assert.Equal(Convert.FromBase64String(OnePixelPng), decoded.Bytes);
    }

    [Fact]
    public void APercentEncodedSvg_IsRead()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4\" height=\"2\"></svg>";

        var decoded = DataImageUri.Decode("data:image/svg+xml;charset=utf-8," + Uri.EscapeDataString(svg));

        Assert.Null(decoded.Problem);
        Assert.Equal(MediaFormat.Svg, decoded.Format);
        Assert.Equal(svg, Encoding.UTF8.GetString(decoded.Bytes!));
    }

    [Fact]
    public void MalformedBase64_IsUnreadable()
    {
        var decoded = DataImageUri.Decode("data:image/png;base64,not*base64!");

        Assert.Equal(EmbeddedImageProblems.Unreadable, decoded.Problem);
        Assert.Null(decoded.Bytes);
    }

    [Theory]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("data:application/octet-stream;base64,AAAA")]
    [InlineData("data:,hello")]
    public void DataThatIsNotAnImage_IsRefused(string src)
    {
        var decoded = DataImageUri.Decode(src);

        Assert.Equal(EmbeddedImageProblems.NotImageData, decoded.Problem);
    }

    [Fact]
    public void ImageDataOfAnotherFormat_IsRefused()
    {
        // "BM" is the start of a Windows bitmap, which the library does not take.
        var decoded = DataImageUri.Decode("data:image/bmp;base64," + Convert.ToBase64String("BM\0\0\0\0\0\0"u8.ToArray()));

        Assert.Equal(EmbeddedImageProblems.NotSupported, decoded.Problem);
    }

    [Fact]
    public void ImageDataOverTheSizeLimit_IsRefused_WithoutDecodingIt()
    {
        var payload = new string('A', (int)(MediaUploadLimits.MaxFileBytes / 3 * 4) + 8);

        var decoded = DataImageUri.Decode("data:image/png;base64," + payload);

        Assert.Equal(EmbeddedImageProblems.TooLarge, decoded.Problem);
        Assert.Null(decoded.Bytes);
    }

    [Theory]
    [InlineData("data:image/png;base64,")]
    [InlineData("data:image/png;base64")]
    public void EmptyOrCutOffData_IsRefused(string src)
    {
        var decoded = DataImageUri.Decode(src);

        Assert.NotNull(decoded.Problem);
        Assert.Null(decoded.Bytes);
    }
}
