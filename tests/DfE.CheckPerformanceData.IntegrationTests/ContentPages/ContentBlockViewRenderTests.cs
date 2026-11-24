using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Widget;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;
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

// Renders a content block through the real Razor views. A block's value is rich text, so its
// library images get the same signed responsive copies as the rich-text widget. A block has no
// version that renders with its own files: every view shows the newest version of each image.
public sealed class ContentBlockViewRenderTests
{
    private static readonly Guid PngId = Guid.Parse("33333333-38a9-4a65-9e3a-5f8f2f0f7a11");
    private readonly IMediaVersionResolver _resolver = Substitute.For<IMediaVersionResolver>();

    public ContentBlockViewRenderTests()
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

    private static string Image(Guid id) =>
        $"<p><img data-media-id=\"{id}\" src=\"/media/{id}/1/chart.png\" alt=\"Results chart\"></p>";

    private static EditableContentViewModel Block(string valueHtml) => new()
    {
        Key = "block-render",
        Value = valueHtml,
        ValueHtml = valueHtml,
        HasSavedContent = true,
    };

    [Fact]
    public async Task ABlockLibraryImage_GetsSignedResponsiveCopies_OfTheNewestVersion()
    {
        var html = await RenderAsync("~/Views/Shared/Components/EditableContent/Default.cshtml", Block(Image(PngId)));

        Assert.Contains($"src=\"/media/{PngId:D}/2/chart.webp?w=960&amp;sig=", html);
        Assert.Contains("srcset=\"/media/", html);
        Assert.Contains("alt=\"Results chart\"", html);
        await _resolver.Received().ResolveAsync(Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ABlockImageNoLongerInTheLibrary_IsDroppedFromThePage()
    {
        var html = await RenderAsync("~/Views/Shared/Components/EditableContent/Default.cshtml",
            Block("<p>Before</p>" + Image(Guid.NewGuid())));

        Assert.DoesNotContain("<img", html);
        Assert.Contains("Before", html);
    }

    // The version history previews each version with the newest files, and warns about an image
    // that is no longer in the library.
    [Fact]
    public async Task TheVersionHistory_ShowsLibraryImages_AndWarnsAboutAMissingOne()
    {
        var model = new ContentBlockVersionsViewModel
        {
            Block = new ContentBlockDto { Id = 1, Key = "block-render", BlockType = "Content", Value = "" },
            Versions =
            [
                new ContentBlockVersionDto { Id = 11, VersionNumber = 2, Value = "", ValueHtml = Image(PngId), CreatedAt = DateTime.UtcNow },
                new ContentBlockVersionDto { Id = 10, VersionNumber = 1, Value = "", ValueHtml = Image(Guid.NewGuid()), CreatedAt = DateTime.UtcNow },
            ],
        };

        var html = await RenderAsync("~/Views/ContentBlock/Versions.cshtml", model);

        Assert.Contains($"src=\"/media/{PngId:D}/2/chart.webp?w=960&amp;sig=", html);
        Assert.Contains("This image is no longer in the media library", html);
    }

    private async Task<string> RenderAsync<TModel>(string viewPath, TModel model)
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

        var view = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate {viewPath}. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var dictionary = new ViewDataDictionary<TModel>(
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
