using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Widget;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.Guidance;
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

// Renders the Image widget's edit form through the real Razor view engine. The form must post
// every prop as a plain field, so it works without JavaScript. It also carries what the image
// editor script needs about the chosen image: its name, thumbnail, crop surface and whether a
// crop applies to it.
public sealed class ImageWidgetEditorRenderTests
{
    private static readonly Guid PngId = Guid.Parse("33333333-38a9-4a65-9e3a-5f8f2f0f7a11");
    private static readonly Guid SvgId = Guid.Parse("44444444-38a9-4a65-9e3a-5f8f2f0f7a11");
    private readonly IMediaVersionResolver _resolver = Substitute.For<IMediaVersionResolver>();

    public ImageWidgetEditorRenderTests()
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
        var asset = new MediaAssetDto(id, slug + (contentType == "image/png" ? ".png" : ".svg"), slug, "Library <alt>", null, null, [], 4, version,
            DateTime.UtcNow, "u", DateTime.UtcNow, "u");
        return new ResolvedMedia(asset, version);
    }

    private static WidgetNode Widget(Guid? id, Action<JsonObject>? change = null)
    {
        var props = WidgetRegistry.CreateDefaultProps("image");
        props["mediaId"] = id?.ToString() ?? "";
        change?.Invoke(props);
        return new WidgetNode { Type = "image", Props = props };
    }

    private static int Count(string html, string text) => Regex.Matches(html, Regex.Escape(text)).Count;

    [Fact]
    public async Task TheForm_PostsEveryProp_AsAPlainField()
    {
        var html = await RenderAsync(Widget(PngId));

        foreach (var prop in new[] { "mediaId", "alt", "caption", "credit", "href", "crop", "aspect", "focal" })
            Assert.Equal(1, Count(html, $"name=\"props[{prop}]\""));
        // The checkbox and the hidden "false" after it.
        Assert.Equal(2, Count(html, "name=\"props[decorative]\""));
        Assert.Equal(4, Count(html, "name=\"props[width]\""));
        Assert.Equal(4, Count(html, "name=\"props[align]\""));
    }

    [Fact]
    public async Task WidthAndAlignment_AreGovukRadios_WithTheFieldIdOnTheFirst()
    {
        var html = await RenderAsync(Widget(PngId, p => { p["width"] = "one-half"; p["align"] = "centre"; }));

        Assert.Matches("<input class=\"govuk-radios__input\" id=\"wf-width-0\\.0\" name=\"props\\[width\\]\" type=\"radio\" value=\"full\"", html);
        Assert.Matches("<input class=\"govuk-radios__input\" id=\"wf-align-0\\.0\" name=\"props\\[align\\]\" type=\"radio\" value=\"\"", html);
        Assert.Matches("value=\"one-half\"\\s+checked", html);
        Assert.Matches("value=\"centre\"\\s+checked", html);
        foreach (var label in new[] { "Full width", "Two-thirds", "One-half", "One-third", "None", "Left", "Centre", "Right" })
            Assert.Contains($">{label}", html);
        Assert.Contains("<legend class=\"govuk-fieldset__legend govuk-fieldset__legend--s\">Width</legend>", html);
        Assert.Contains("<legend class=\"govuk-fieldset__legend govuk-fieldset__legend--s\">Alignment</legend>", html);
    }

    [Fact]
    public async Task WithoutJavaScript_TheEditorCanPasteAnId_FromTheLibraryInANewTab()
    {
        var html = await RenderAsync(Widget(null));

        Assert.Contains("id=\"wf-mediaId-0.0\"", html);
        Assert.Contains("<a class=\"govuk-link\" href=\"/admin/media\" target=\"_blank\" rel=\"noopener noreferrer\">Open the media library in a new tab</a>", html);
        // The script shows the picker button; without the script it stays hidden.
        Assert.Matches("<button[^>]*data-image-editor-choose[^>]*hidden", html);
    }

    [Fact]
    public async Task TheChosenImage_IsDescribedForTheScript()
    {
        var html = await RenderAsync(Widget(PngId));

        Assert.Contains("data-image-editor", html);
        Assert.Contains("data-media-file-name=\"school-photo.png\"", html);
        Assert.Contains("data-media-alt=\"Library &lt;alt&gt;\"", html);
        Assert.Contains("data-media-can-crop=\"true\"", html);
        Assert.Matches($"data-media-thumb-url=\"/media/{PngId:D}/4/school-photo\\.webp\\?w=320&amp;sig=", html);
        Assert.Matches($"data-media-edit-url=\"/media/{PngId:D}/4/school-photo\\.webp\\?w=960&amp;sig=", html);
        Assert.Contains("data-preview-url=\"/admin/media/picker/preview\"", html);
    }

    [Fact]
    public async Task AnSvg_IsMarkedAsNotCroppable()
    {
        var html = await RenderAsync(Widget(SvgId));

        Assert.Contains("data-media-can-crop=\"false\"", html);
        Assert.Contains("Crop and focal point are not available for this image", html);
    }

    [Fact]
    public async Task TheCropPresets_DoNotPostAsProps_AndNoInlineHandlersAreWritten()
    {
        var html = await RenderAsync(Widget(PngId));

        foreach (var preset in new[] { "Free", "16:9", "4:3", "1:1", "3:2" })
            Assert.Contains($">{preset}</label>", html);
        Assert.Contains("data-image-editor-crop-reset", html);
        Assert.Contains("data-image-editor-focal-area", html);
        Assert.DoesNotMatch("\\son[a-z]+=\"", html);
    }

    private async Task<string> RenderAsync(WidgetNode widget)
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

        const string viewPath = "~/Views/Shared/ContentPages/_EditWidget.cshtml";
        var view = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: false);
        Assert.True(view.Success, $"Could not locate {viewPath}.");

        var dictionary = new ViewDataDictionary<EditWidgetModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = new EditWidgetModel("/admin/pages/1", "0.0", widget)
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(actionContext, view.View, dictionary, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);
        return writer.ToString();
    }

    private sealed class FixedKey : IMediaUrlSigningKey
    {
        public byte[] GetKey() => Enumerable.Repeat((byte)5, 32).ToArray();
    }
}
