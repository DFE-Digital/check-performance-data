using System.Text.Json.Nodes;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentPages;
using DfE.CheckPerformanceData.Application.HomeBanners;
using DfE.CheckPerformanceData.Application.Media.Serving;
using DfE.CheckPerformanceData.Application.Media.Widget;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.PageTree;
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

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

// Renders Views/Page/Content.cshtml through the real Razor view engine to prove the template
// correctly wires up RenderedPageViewModel: H1 heading, widget tree via _ContentNodes,
// and rich-text sanitisation (no script survives). The side-nav is a widget in its own
// right (PageNav) — placed by authors, not injected by the layout — so this test asserts
// the always-on template concerns only.
public sealed class ContentTemplateRenderTests
{
    private readonly IMediaVersionResolver _mediaResolver = EmptyResolver();

    private static IMediaVersionResolver EmptyResolver()
    {
        var resolver = Substitute.For<IMediaVersionResolver>();
        resolver.ResolveAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, ResolvedMedia>());
        return resolver;
    }

    private static WidgetNode ImageWidget(Guid id) =>
        new() { Type = "image", Props = new JsonObject { ["mediaId"] = id.ToString(), ["alt"] = "x" } };

    [Fact]
    public async Task APage_LoadsAllItsImagesInOneCall_WithTheCurrentVersions_AndHidesAMissingOne()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var html = await RenderContentViewAsync(new RenderedPageViewModel
        {
            Title = "Images", PageType = "content", Content = [ImageWidget(first), ImageWidget(second)]
        });

        await _mediaResolver.Received(1).ResolveAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.Count() == 2), null, Arg.Any<CancellationToken>());
        Assert.DoesNotContain("no longer in the media library", html);
    }

    [Fact]
    public async Task AVersionHistoryPreview_AsksForTheImageVersionsRecordedForThatPageVersion_AndWarnsAboutAMissingOne()
    {
        var pageVersion = Guid.NewGuid();

        var html = await RenderContentViewAsync(new RenderedPageViewModel
        {
            Title = "Images", PageType = "content", Content = [ImageWidget(Guid.NewGuid())],
            IsPreview = true, MediaPageVersionId = pageVersion
        });

        await _mediaResolver.Received().ResolveAsync(Arg.Any<IEnumerable<Guid>>(), pageVersion, Arg.Any<CancellationToken>());
        await _mediaResolver.DidNotReceive().ResolveAsync(Arg.Any<IEnumerable<Guid>>(), null, Arg.Any<CancellationToken>());
        Assert.Contains("This image is no longer in the media library", html);
    }

    [Fact]
    public async Task RendersHeadingNavWidgetsAndSanitisesRichText()
    {
        IReadOnlyList<ContentNode> content =
        [
            new WidgetNode
            {
                Type   = "heading",
                Anchor = "overview",
                Props  = new JsonObject { ["level"] = 2, ["text"] = "Overview" }
            },
            new WidgetNode
            {
                Type  = "richtext",
                Props = new JsonObject { ["html"] = "<p>Safe text<script>alert(1)</script></p>" }
            }
        ];

        IReadOnlyList<ContentNavItem> nav =
        [
            new ContentNavItem("Overview", "#overview", [])
        ];

        var model = new RenderedPageViewModel
        {
            Title    = "Test Content Page",
            PageType = "content",
            Content  = content,
            Nav      = nav
        };

        var html = await RenderContentViewAsync(model);

        // View's own H1 carries the page title in GDS xl heading style.
        Assert.Contains("govuk-heading-xl", html);
        Assert.Contains("Test Content Page", html);

        // _Heading widget rendered via _ContentNodes → _Widget → _Heading.
        Assert.Contains("govuk-heading-l", html);
        Assert.Contains("id=\"overview\"", html);
        Assert.Contains("Overview", html);

        // Rich text: safe text survives; the malicious payload is stripped. Asserting the
        // payload itself, not the substring "<script" — the layout legitimately includes its
        // own <script src="/js/…"> tags for widget-editor JS, which is not what we're guarding
        // against here.
        Assert.Contains("Safe text", html);
        Assert.DoesNotContain("alert(1)", html);
    }

    [Fact]
    public async Task RendersPreviewBanner_WhenIsPreviewTrue()
    {
        var model = new RenderedPageViewModel
        {
            Title    = "Draft Page",
            PageType = "content",
            Content  = [],
            Nav      = [],
            IsPreview = true
        };

        var html = await RenderContentViewAsync(model);

        Assert.Contains("govuk-notification-banner", html);
        Assert.Contains("not published", html);
    }

    [Fact]
    public async Task DoesNotRenderPreviewBanner_WhenIsPreviewFalse()
    {
        var model = new RenderedPageViewModel
        {
            Title    = "Published Page",
            PageType = "content",
            Content  = [],
            Nav      = [],
            IsPreview = false
        };

        var html = await RenderContentViewAsync(model);

        Assert.DoesNotContain("govuk-notification-banner", html);
        Assert.DoesNotContain("not published", html);
    }

    // ── #588 Home page banners on the guidance front page ─────────────────────────────

    [Fact]
    public async Task RendersHomeBanners_WhenShowHomeBannersTrue()
    {
        var model = new RenderedPageViewModel
        {
            Title           = "Guidance",
            PageType        = "content",
            Content         = [],
            Nav             = [],
            ShowHomeBanners = true
        };
        IReadOnlyList<HomeBannerDto> banners =
        [
            new() { Id = 42, Heading = "Apply by 31 October", BodyHtml = "<p>Do it now.</p>", IsEnabled = true }
        ];

        var html = await RenderContentViewAsync(model, banners);

        Assert.Contains("home-banner-42-heading", html);
        Assert.Contains("Apply by 31 October", html);
        // Banners sit above the template-emitted H1.
        Assert.True(
            html.IndexOf("home-banner-42-heading", StringComparison.Ordinal)
                < html.IndexOf("govuk-heading-xl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DoesNotRenderHomeBanners_WhenShowHomeBannersFalse()
    {
        var model = new RenderedPageViewModel
        {
            Title           = "My Page",
            PageType        = "content",
            Content         = [],
            Nav             = [],
            ShowHomeBanners = false
        };
        IReadOnlyList<HomeBannerDto> banners =
        [
            new() { Id = 42, Heading = "Apply by 31 October", BodyHtml = "<p>Do it now.</p>", IsEnabled = true }
        ];

        var html = await RenderContentViewAsync(model, banners);

        Assert.DoesNotContain("home-banner-42-heading", html);
        Assert.DoesNotContain("Apply by 31 October", html);
    }

    // Spins up a minimal MVC host to obtain a wired view engine, then renders
    // Views/Page/Content.cshtml with isMainPage:false (bypasses _ViewStart / layout lookup).
    private async Task<string> RenderContentViewAsync(
        RenderedPageViewModel model, IReadOnlyList<HomeBannerDto>? banners = null)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(PageController).Assembly);
                    // The rich-text partial injects the sanitiser.
                    services.AddScoped<IHtmlRenderingService, HtmlRenderingService>();
                    // The HomeBanners view component reads the live set from here.
                    var bannerService = Substitute.For<IHomeBannerService>();
                    bannerService.GetLiveAsync().Returns(banners ?? []);
                    services.AddScoped(_ => bannerService);
                    // The Image widget resolves library images and builds their URLs.
                    services.AddSingleton(_mediaResolver);
                    services.AddSingleton(Substitute.For<IMediaUrlBuilder>());
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine      = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var routeData   = new RouteData();
        routeData.Values["controller"] = "Page";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        // isMainPage:false → no _ViewStart / layout processing; just the view body.
        var view = viewEngine.FindView(actionContext, "Content", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate Content view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var viewData = new ViewDataDictionary<RenderedPageViewModel>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer      = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
