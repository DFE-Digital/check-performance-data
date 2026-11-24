using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Widget;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.PageTree;
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

// Renders the wiki page template through the real Razor view engine. A wiki body is rich text, so
// its library images get the same signed responsive copies as the rich-text widget on a content page.
public sealed class WikiViewRenderTests
{
    private static readonly Guid PngId = Guid.Parse("33333333-38a9-4a65-9e3a-5f8f2f0f7a11");
    private readonly IMediaVersionResolver _resolver = Substitute.For<IMediaVersionResolver>();

    public WikiViewRenderTests()
    {
        var version = new MediaAssetVersionDto(PngId, 2, MediaBlobKeys.Original(PngId, 2), "image/png", 1600, 800, 100, "sha",
            false, DateTime.UtcNow, "u");
        var asset = new MediaAssetDto(PngId, "chart.png", "chart", "Library alt", null, null, [], 2, version,
            DateTime.UtcNow, "u", DateTime.UtcNow, "u");
        var library = new Dictionary<Guid, ResolvedMedia> { [PngId] = new(asset, version) };
        _resolver.ResolveAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci => (IReadOnlyDictionary<Guid, ResolvedMedia>)ci.Arg<IEnumerable<Guid>>()
                .Where(library.ContainsKey).ToDictionary(id => id, id => library[id]));
    }

    private static RenderedPageViewModel Wiki(string body, bool preview = false, Guid? pageVersion = null) => new()
    {
        Title = "Wiki page",
        PageType = "wiki",
        WikiHtml = body,
        Nav = [],
        IsPreview = preview,
        MediaPageVersionId = pageVersion
    };

    [Fact]
    public async Task AWikiLibraryImage_GetsSignedResponsiveCopies()
    {
        var html = await RenderAsync(Wiki(
            $"<p><img data-media-id=\"{PngId}\" src=\"/media/{PngId}/1/chart.png\" alt=\"Results chart\"></p>"));

        Assert.Contains($"src=\"/media/{PngId:D}/2/chart.webp?w=960&amp;sig=", html);
        Assert.Contains("srcset=\"/media/", html);
        Assert.Contains("loading=\"lazy\"", html);
        Assert.Contains("alt=\"Results chart\"", html);
    }

    [Fact]
    public async Task AWikiImageNoLongerInTheLibrary_IsDroppedFromThePage_AndWarnedAboutInAPreview()
    {
        var body = $"<p>Before</p><p><img data-media-id=\"{Guid.NewGuid()}\" src=\"/media/x.png\" alt=\"Gone\"></p>";

        var page = await RenderAsync(Wiki(body));
        Assert.DoesNotContain("<img", page);
        Assert.Contains("Before", page);

        var preview = await RenderAsync(Wiki(body, preview: true));
        Assert.Contains("This image is no longer in the media library", preview);
    }

    [Fact]
    public async Task AHistoricWikiVersion_AsksForTheImageVersionsRecordedForIt()
    {
        var pageVersion = Guid.NewGuid();

        await RenderAsync(Wiki($"<img data-media-id=\"{PngId}\" src=\"/media/{PngId}/1/a.png\" alt=\"x\">",
            preview: true, pageVersion: pageVersion));

        await _resolver.Received().ResolveAsync(Arg.Any<IEnumerable<Guid>>(), pageVersion, Arg.Any<CancellationToken>());
    }

    private async Task<string> RenderAsync(RenderedPageViewModel model)
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
                    services.AddSingleton<IHtmlRenderingService, HtmlRenderingService>();
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

        const string viewPath = "~/Views/Page/Wiki.cshtml";
        var view = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate {viewPath}. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var dictionary = new ViewDataDictionary<RenderedPageViewModel>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
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
