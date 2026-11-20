using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Widget;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.PageTree;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.IntegrationTests.ContentPages;

// Renders the Image widget through the real Razor view engine, with the image versions given by a
// stub resolver and URLs from the real URL builder. Covers the markup a visitor's browser gets:
// the figure, the signed srcset, the link, the caption and credit, and what a missing image shows.
public sealed class ImageWidgetViewRenderTests
{
    private static readonly Guid PngId = Guid.Parse("11111111-38a9-4a65-9e3a-5f8f2f0f7a11");
    private static readonly Guid SvgId = Guid.Parse("22222222-38a9-4a65-9e3a-5f8f2f0f7a11");
    private readonly IMediaVersionResolver _resolver = Substitute.For<IMediaVersionResolver>();

    public ImageWidgetViewRenderTests()
    {
        var library = new Dictionary<Guid, ResolvedMedia>
        {
            [PngId] = Media(PngId, "image/png", 2000, 1000, "school-photo"),
            [SvgId] = Media(SvgId, "image/svg+xml", 64, 32, "logo"),
        };
        _resolver.ResolveAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyDictionary<Guid, ResolvedMedia>)ci.Arg<IEnumerable<Guid>>()
                .Where(library.ContainsKey).ToDictionary(id => id, id => library[id]));
    }

    private static ResolvedMedia Media(Guid id, string contentType, int width, int height, string slug)
    {
        var version = new MediaAssetVersionDto(id, 4, MediaBlobKeys.Original(id, 4), contentType, width, height, 100, "sha",
            false, DateTime.UtcNow, "u");
        var asset = new MediaAssetDto(id, slug, slug, "Library alt", null, null, [], 4, version,
            DateTime.UtcNow, "u", DateTime.UtcNow, "u");
        return new ResolvedMedia(asset, version);
    }

    private static WidgetNode Widget(Guid? id, Action<JsonObject>? change = null)
    {
        var props = WidgetRegistry.CreateDefaultProps("image");
        props["mediaId"] = id?.ToString() ?? "";
        props["alt"] = "Pupils <in> a classroom";
        change?.Invoke(props);
        return new WidgetNode { Type = "image", Props = props };
    }

    [Fact]
    public async Task APng_RendersAFigure_WithASignedRenditionSrcsetAndItsShape()
    {
        var html = await RenderAsync(Widget(PngId, p => { p["width"] = "one-half"; p["align"] = "right"; p["aspect"] = "16:9"; }));

        Assert.Contains("<figure class=\"app-image app-image--one-half app-image--align-right\"", html);
        Assert.Contains($"src=\"/media/{PngId:D}/4/school-photo.webp?w=640&amp;ar=16:9&amp;sig=", html);
        Assert.Contains("srcset=\"/media/", html);
        Assert.Contains("1778w\"", html);
        Assert.Contains("sizes=\"(min-width: 1020px) 480px, (min-width: 641px) 50vw, 100vw\"", html);
        Assert.Contains("width=\"1778\"", html);
        Assert.Contains("height=\"1000\"", html);
        Assert.Contains("loading=\"lazy\"", html);
        Assert.Contains("decoding=\"async\"", html);
        // Alt text is escaped, never written raw.
        Assert.Contains("alt=\"Pupils &lt;in&gt; a classroom\"", html);
        Assert.DoesNotContain("<in>", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("<figcaption", html);
    }

    [Fact]
    public async Task AnSvg_UsesTheOriginal_WithNoSrcsetOrSizes()
    {
        var html = await RenderAsync(Widget(SvgId, p => p["crop"] = "0,0,0.5,1"));

        Assert.Contains($"src=\"/media/{SvgId:D}/4/logo.svg\"", html);
        Assert.DoesNotContain("srcset", html);
        Assert.DoesNotContain("sizes", html);
        Assert.Contains("width=\"64\"", html);
        Assert.Contains("height=\"32\"", html);
    }

    [Fact]
    public async Task ADecorativeImage_HasAnEmptyAltAttribute()
    {
        var html = await RenderAsync(Widget(PngId, p => p["decorative"] = "true"));

        Assert.Contains("alt=\"\"", html);
        Assert.DoesNotContain("Pupils", html);
    }

    [Fact]
    public async Task ALinkedImage_IsWrappedInAGovukLink()
    {
        var html = await RenderAsync(Widget(PngId, p => p["href"] = "/guidance/key-dates"));

        Assert.Contains("<a class=\"govuk-link\" href=\"/guidance/key-dates\">", html);
        Assert.True(html.IndexOf("<a ", StringComparison.Ordinal) < html.IndexOf("<img", StringComparison.Ordinal));
        Assert.True(html.IndexOf("</a>", StringComparison.Ordinal) > html.IndexOf("<img", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnsafeStoredLink_IsNotWritten()
    {
        var html = await RenderAsync(Widget(PngId, p => p["href"] = "javascript:alert(1)"));

        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("javascript:", html);
    }

    [Theory]
    [InlineData("Year 6 maths", "Photo: DfE", "Year 6 maths &mdash; Photo: DfE")]
    [InlineData("Year 6 maths", "", "Year 6 maths")]
    [InlineData("", "Photo: DfE", "Photo: DfE")]
    public async Task TheCaptionAndCredit_AreWrittenInAFigcaption(string caption, string credit, string expected)
    {
        var html = await RenderAsync(Widget(PngId, p => { p["caption"] = caption; p["credit"] = credit; }));

        Assert.Contains($"<figcaption class=\"govuk-body-s\">{expected}</figcaption>", html);
    }

    [Fact]
    public async Task AHostileCaption_IsEscaped()
    {
        var html = await RenderAsync(Widget(PngId, p => p["caption"] = "<script>alert(1)</script>"));

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Fact]
    public async Task AMissingImage_RendersNothing_OnAPublicPage()
    {
        var html = await RenderAsync(Widget(Guid.NewGuid()));

        Assert.Equal("", html.Trim());
    }

    [Fact]
    public async Task AMissingImage_ShowsAWarning_InPreviewAndTheEditor()
    {
        var html = await RenderAsync(Widget(Guid.NewGuid()), new MediaRenderContext(null, ShowEditorNotices: true));

        Assert.Contains("govuk-warning-text", html);
        Assert.Contains("This image is no longer in the media library", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public async Task AWidgetWithNoImageChosenYet_RendersNothing_OnAPublicPage_AndAHintInTheEditor()
    {
        Assert.Equal("", (await RenderAsync(Widget(null))).Trim());

        var editor = await RenderAsync(Widget(null), new MediaRenderContext(null, ShowEditorNotices: true));
        Assert.Contains("No image selected", editor);
        Assert.DoesNotContain("no longer in the media library", editor);
    }

    [Fact]
    public async Task AHistoricPageVersion_AsksTheResolverForTheVersionsRecordedForIt()
    {
        var pageVersion = Guid.NewGuid();

        await RenderAsync(Widget(PngId), new MediaRenderContext(pageVersion, ShowEditorNotices: true));

        await _resolver.Received().ResolveAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.Contains(PngId)), pageVersion, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALivePage_AsksTheResolverForTheCurrentVersions()
    {
        await RenderAsync(Widget(PngId));

        await _resolver.Received().ResolveAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.Contains(PngId)), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheWidgetDispatcher_RendersTheImagePartial()
    {
        var html = await RenderAsync(Widget(PngId), viewPath: "~/Views/Shared/ContentPages/_Widget.cshtml");

        Assert.Contains("<figure class=\"app-image app-image--full\"", html);
    }

    private async Task<string> RenderAsync(WidgetNode model, MediaRenderContext? context = null,
        string viewPath = "~/Views/Shared/ContentPages/Widgets/_Image.cshtml")
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(PageController).Assembly);
                    services.AddGovUkFrontend();
                    services.AddSingleton(_resolver);
                    services.AddSingleton<IMediaUrlBuilder>(new MediaUrlBuilder(new MediaUrlSigner(new FixedKey())));
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        var view = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate {viewPath}. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var dictionary = new ViewDataDictionary<WidgetNode>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        if (context is not null) dictionary[MediaRenderContext.ViewDataKey] = context;
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, dictionary, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }

    private sealed class FixedKey : IMediaUrlSigningKey
    {
        public byte[] GetKey() => Enumerable.Repeat((byte)5, 32).ToArray();
    }
}
