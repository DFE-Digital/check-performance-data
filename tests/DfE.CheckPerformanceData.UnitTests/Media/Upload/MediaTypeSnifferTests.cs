using System.Text;
using DfE.CheckPerformanceData.Application.Media.Upload;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Upload;

// The type comes from the bytes alone. Anything that is not one of the five formats is refused,
// whatever its name says.
public sealed class MediaTypeSnifferTests
{
    [Fact]
    public void RecognisesEachRasterFormat()
    {
        Assert.Equal(MediaFormat.Png, MediaTypeSniffer.Sniff(ImageFixtures.Png()));
        Assert.Equal(MediaFormat.Jpeg, MediaTypeSniffer.Sniff(ImageFixtures.Jpeg()));
        Assert.Equal(MediaFormat.Gif, MediaTypeSniffer.Sniff(ImageFixtures.Gif(2, 2, [0])));
        Assert.Equal(MediaFormat.WebP, MediaTypeSniffer.Sniff(ImageFixtures.WebPLossless()));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("﻿  \r\n<?xml version=\"1.0\"?><svg/>")]
    public void TreatsMarkupAsAnSvgCandidate(string text)
    {
        // Markup is only a candidate: the SVG sanitiser still has to parse it and find an <svg> root.
        Assert.Equal(MediaFormat.Svg, MediaTypeSniffer.Sniff(Encoding.UTF8.GetBytes(text)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("%PDF-1.7")]
    [InlineData("PK\u0003\u0004")]
    [InlineData("RIFF\0\0\0\0WAVE")]
    [InlineData("GIF90a")]
    public void RefusesAnythingElse(string text)
    {
        Assert.Null(MediaTypeSniffer.Sniff(Encoding.Latin1.GetBytes(text)));
    }
}
