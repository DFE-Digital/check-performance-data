using System.Text;
using DfE.CheckPerformanceData.Application.Media.Upload;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Upload;

// SVG is markup a browser can run, so uploads are rebuilt from an allow-list: anything that could
// run script, load another file or reach outside the image is removed, and a file that will not
// parse, or uses a DTD, is refused outright.
public sealed class SvgSanitiserTests
{
    private const string Ns = "xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"";

    private static string Clean(string svg)
    {
        var result = SvgSanitiser.Sanitise(Encoding.UTF8.GetBytes(svg));
        Assert.True(result.IsAccepted, result.Rejection);
        Assert.Equal(MediaFormat.Svg, result.File!.Format);
        return Encoding.UTF8.GetString(result.File.Bytes);
    }

    private static void AssertRefused(string svg)
    {
        var result = SvgSanitiser.Sanitise(Encoding.UTF8.GetBytes(svg));
        Assert.False(result.IsAccepted);
        Assert.Equal(MediaRejections.SvgUnreadable, result.Rejection);
    }

    [Fact]
    public void KeepsOrdinaryDrawing()
    {
        var output = Clean($"<svg {Ns} width=\"100\" height=\"50\"><g fill=\"#00703c\"><rect x=\"1\" y=\"2\" width=\"3\" height=\"4\"/><circle cx=\"5\" cy=\"5\" r=\"2\" style=\"stroke: red; stroke-width: 2\"/></g><text x=\"0\" y=\"10\">A &amp; B</text></svg>");

        Assert.Contains("<rect x=\"1\" y=\"2\" width=\"3\" height=\"4\"", output);
        Assert.Contains("fill=\"#00703c\"", output);
        Assert.Contains("stroke: red", output);
        Assert.Contains("A &amp; B", output);
        Assert.StartsWith("<svg", output);
    }

    [Fact]
    public void RemovesScriptElements()
    {
        var output = Clean($"<svg {Ns}><script>alert(1)</script><script xlink:href=\"https://evil.test/x.js\"/><rect width=\"1\" height=\"1\"/></svg>");

        Assert.DoesNotContain("script", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", output);
        Assert.Contains("<rect", output);
    }

    [Fact]
    public void RemovesEventHandlerAttributes()
    {
        var output = Clean($"<svg {Ns} onload=\"alert(1)\"><rect width=\"1\" height=\"1\" onclick=\"alert(2)\" ONMOUSEOVER=\"alert(3)\"/></svg>");

        Assert.DoesNotContain("alert", output);
        Assert.DoesNotContain("onload", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<rect", output);
    }

    [Fact]
    public void RemovesForeignObjectAndEverythingInIt()
    {
        var output = Clean($"<svg {Ns}><foreignObject><div xmlns=\"http://www.w3.org/1999/xhtml\"><iframe src=\"https://evil.test\"/></div></foreignObject></svg>");

        Assert.DoesNotContain("foreignObject", output);
        Assert.DoesNotContain("iframe", output);
        Assert.DoesNotContain("evil", output);
    }

    [Fact]
    public void RemovesElementsFromOtherNamespaces()
    {
        var output = Clean($"<svg {Ns}><html:script xmlns:html=\"http://www.w3.org/1999/xhtml\">alert(1)</html:script><rect width=\"1\" height=\"1\"/></svg>");

        Assert.DoesNotContain("alert", output);
        Assert.Contains("<rect", output);
    }

    [Theory]
    [InlineData("<use xlink:href=\"https://evil.test/sprite.svg#icon\"/>")]
    [InlineData("<use href=\"//evil.test/sprite.svg#icon\"/>")]
    [InlineData("<use href=\"sprite.svg#icon\"/>")]
    [InlineData("<image href=\"https://evil.test/track.png\"/>")]
    [InlineData("<image xlink:href=\"data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=\"/>")]
    [InlineData("<image href=\"data:text/html,&lt;script&gt;alert(1)&lt;/script&gt;\"/>")]
    [InlineData("<a href=\"https://evil.test\"><rect width=\"1\" height=\"1\"/></a>")]
    public void RemovesHrefsThatPointOutsideTheDocument(string element)
    {
        var output = Clean($"<svg {Ns}>{element}</svg>");

        Assert.DoesNotContain("href", output);
        Assert.DoesNotContain("evil", output);
        Assert.DoesNotContain("data:", output);
    }

    [Fact]
    public void KeepsFragmentHrefsAndRasterDataImages()
    {
        var output = Clean($"<svg {Ns}><defs><circle id=\"dot\" r=\"2\"/></defs><use xlink:href=\"#dot\"/><use href=\"#dot\"/><image href=\"data:image/png;base64,iVBORw0KGgo=\"/></svg>");

        Assert.Contains("xlink:href=\"#dot\"", output);
        Assert.Contains("<use href=\"#dot\"", output);
        Assert.Contains("href=\"data:image/png;base64,iVBORw0KGgo=\"", output);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData(" java\tscript:alert(1)")]
    [InlineData("java&#x0A;script:alert(1)")]
    [InlineData("&#106;avascript:alert(1)")]
    public void RemovesJavaScriptUrls(string href)
    {
        var output = Clean($"<svg {Ns}><a href=\"{href}\"><text>x</text></a><a xlink:href=\"{href}\"><text>y</text></a></svg>");

        Assert.DoesNotContain("alert", output);
        Assert.DoesNotContain("script", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DropsXmlStylesheetProcessingInstructions()
    {
        var output = Clean($"<?xml version=\"1.0\"?><?xml-stylesheet href=\"https://evil.test/x.css\" type=\"text/css\"?><svg {Ns}><rect width=\"1\" height=\"1\"/></svg>");

        Assert.DoesNotContain("xml-stylesheet", output);
        Assert.DoesNotContain("evil", output);
    }

    [Theory]
    // A billion-laughs entity bomb.
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\"><!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">]><svg xmlns=\"http://www.w3.org/2000/svg\"><text>&c;</text></svg>")]
    // An external entity (XXE) that would read a local file.
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><svg xmlns=\"http://www.w3.org/2000/svg\"><text>&xxe;</text></svg>")]
    // Any DOCTYPE at all.
    [InlineData("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\"><svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    public void RefusesDtdsAndEntities(string svg)
    {
        AssertRefused(svg);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect>")]
    [InlineData("<html><body>Not an image</body></html>")]
    [InlineData("<svg><rect/></svg>")]
    [InlineData("<math xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("<?xml version=\"1.0\"?>")]
    public void RefusesFilesThatWontParseOrAreNotAnSvg(string svg)
    {
        AssertRefused(svg);
    }

    [Fact]
    public void RefusesDeeplyNestedFiles()
    {
        var depth = MediaUploadLimits.MaxSvgDepth + 5;
        var svg = $"<svg {Ns}>" + string.Concat(Enumerable.Repeat("<g>", depth)) + string.Concat(Enumerable.Repeat("</g>", depth)) + "</svg>";

        AssertRefused(svg);
    }

    [Fact]
    public void RefusesFilesOverTheCharacterLimit()
    {
        var svg = $"<svg {Ns}><desc>{new string('a', MediaUploadLimits.MaxSvgCharacters)}</desc></svg>";

        AssertRefused(svg);
    }

    [Theory]
    [InlineData("<set attributeName=\"href\" to=\"javascript:alert(1)\"/>")]
    [InlineData("<animate attributeName=\"xlink:href\" values=\"https://evil.test\"/>")]
    [InlineData("<set attributeName=\"onclick\" to=\"alert(1)\"/>")]
    [InlineData("<animate attributeName=\"fill\" values=\"red;javascript:alert(1)\"/>")]
    [InlineData("<animate attributeName=\"fill\" from=\"url(https://evil.test/x.svg#p)\" to=\"red\"/>")]
    [InlineData("<animate/>")]
    [InlineData("<animateTransform attributeName=\"href\" type=\"rotate\" to=\"90\"/>")]
    public void RemovesAnimationsThatCouldChangeALinkOrRunScript(string animation)
    {
        var output = Clean($"<svg {Ns}><a href=\"#top\"><rect id=\"top\" width=\"1\" height=\"1\">{animation}</rect></a></svg>");

        Assert.DoesNotContain("<set", output);
        Assert.DoesNotContain("<animate", output);
        Assert.DoesNotContain("evil", output);
        Assert.DoesNotContain("alert", output);
    }

    [Fact]
    public void KeepsAnimationsOfSafeAttributes()
    {
        var output = Clean($"<svg {Ns}><rect width=\"1\" height=\"1\"><animate attributeName=\"opacity\" values=\"0;1\" dur=\"1s\"/><animateTransform attributeName=\"transform\" type=\"rotate\" from=\"0\" to=\"90\" dur=\"2s\"/></rect></svg>");

        Assert.Contains("<animate attributeName=\"opacity\"", output);
        Assert.Contains("<animateTransform attributeName=\"transform\"", output);
    }

    [Theory]
    [InlineData("@import url(https://evil.test/x.css); rect { fill: red }")]
    [InlineData("@\\69mport 'https://evil.test/x.css';")]
    [InlineData("@im/**/port 'x.css';")]
    [InlineData("rect { fill: url(https://evil.test/p.svg#p) }")]
    [InlineData("rect { background: u\\72l(//evil.test/p.png) }")]
    [InlineData("@font-face { font-family: x; src: url('https://evil.test/f.woff') }")]
    // A comment opener inside a string must not hide the rule between it and a later closer.
    [InlineData("a { font-family: \"/*\" } b { fill: url(https://evil.test/x.svg#p) } c { font-family: \"*/\" }")]
    [InlineData("rect { background: image-set(\"https://evil.test/a.png\" 1x) }")]
    public void RemovesStyleElementsThatLoadAnythingExternal(string css)
    {
        var output = Clean($"<svg {Ns}><style>{System.Security.SecurityElement.Escape(css)}</style><rect width=\"1\" height=\"1\"/></svg>");

        Assert.DoesNotContain("evil", output);
        Assert.DoesNotContain("import", output);
        Assert.DoesNotContain("<style", output);
    }

    [Fact]
    public void KeepsSafeStyleElements()
    {
        var output = Clean($"<svg {Ns}><style>rect {{ fill: url(#grad); stroke: #000 }}</style><rect width=\"1\" height=\"1\"/></svg>");

        Assert.Contains("<style>rect { fill: url(#grad); stroke: #000 }</style>", output);
    }

    [Theory]
    [InlineData("fill: url(http://evil.test/p.svg#p); stroke: red")]
    [InlineData("fill: URL( 'https://evil.test/p' ); stroke: red")]
    [InlineData("background-image: u\\72l(//evil.test/p.png); stroke: red")]
    [InlineData("stroke: red; behavior: url(#x); x: expression(alert(1))")]
    [InlineData("stroke: red; fill: url(javascript:alert(1))")]
    [InlineData("stroke: red; background: image-set('https://evil.test/a.png' 1x)")]
    public void RemovesExternalUrlsFromStyleAttributes(string style)
    {
        var output = Clean($"<svg {Ns}><rect width=\"1\" height=\"1\" style=\"{style}\"/></svg>");

        Assert.DoesNotContain("evil", output);
        Assert.DoesNotContain("alert", output);
        Assert.Contains("stroke: red", output);
    }

    [Theory]
    [InlineData("fill")]
    [InlineData("filter")]
    [InlineData("mask")]
    [InlineData("clip-path")]
    [InlineData("marker-end")]
    public void RemovesExternalUrlsFromPresentationAttributes(string attribute)
    {
        var output = Clean($"<svg {Ns}><rect width=\"1\" height=\"1\" {attribute}=\"url(https://evil.test/x.svg#a)\"/><circle r=\"1\" {attribute}=\"url(#local)\"/></svg>");

        Assert.DoesNotContain("evil", output);
        Assert.Contains($"{attribute}=\"url(#local)\"", output);
    }

    [Theory]
    [InlineData("width=\"120\" height=\"80\"", 120, 80)]
    [InlineData("width=\"120.6px\" height=\"80px\"", 121, 80)]
    [InlineData("viewBox=\"0 0 300 150\"", 300, 150)]
    [InlineData("viewBox=\"0,0,300.4,150\" width=\"100%\"", 300, 150)]
    [InlineData("width=\"10em\" viewBox=\"-5 -5 40 20\"", 40, 20)]
    public void ReadsTheSizeFromWidthAndHeightOrTheViewBox(string attributes, int width, int height)
    {
        var result = SvgSanitiser.Sanitise(Encoding.UTF8.GetBytes($"<svg {Ns} {attributes}/>"));

        Assert.Equal(width, result.File!.Width);
        Assert.Equal(height, result.File.Height);
    }

    [Fact]
    public void LeavesTheSizeUnknownWhenTheFileHasNone()
    {
        var result = SvgSanitiser.Sanitise(Encoding.UTF8.GetBytes($"<svg {Ns}><rect width=\"1\" height=\"1\"/></svg>"));

        Assert.Null(result.File!.Width);
        Assert.Null(result.File.Height);
        Assert.False(result.File.IsAnimated);
    }
}
