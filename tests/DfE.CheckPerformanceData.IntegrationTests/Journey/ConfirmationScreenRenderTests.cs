using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Repositories;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.ViewComponents;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.IntegrationTests.Journey;

// Render coverage for the submission confirmation screen's CMS-editable "What happens next"
// block (issue #515 / AB#304140).
//
// The unit view-source tests pin the literal in Views/Journey/Confirmation.cshtml; this file
// proves what a clerk is actually served. The component is invoked exactly the way that view
// invokes it, against the real content-block stack on Testcontainers Postgres, where a fresh
// database auto-provisions the block from the view's own `defaultHtml` — so the first test is
// red until the view's copy is corrected, and doubles as proof that a brand-new environment
// ships the fix (research D1.1).
[Collection(nameof(PostgresCollection))]
public sealed class ConfirmationScreenRenderTests(PostgresFixture fixture)
{
    private const string BlockKey = "journey_confirmation_next_steps";

    // What an environment that already holds the block renders — the value an admin editor
    // would have stored. Deliberately unlike the view's default so the two can be told apart.
    private const string StoredValue =
        "<p class=\"govuk-body\">Hand-edited lead-in:</p>" +
        "<ul class=\"govuk-list govuk-list--bullet\">" +
        "<li>custom wording an editor typed</li>" +
        "</ul>";

    [Fact]
    public async Task FreshEnvironment_provisionsTheCorrectedNextStepsCopy()
    {
        var html = await RenderNextStepsAsync(seedStoredValue: false);

        Assert.Contains("the DfE will review any evidence you provide", html);
        Assert.Contains("After you submit your amendments:", html);
        Assert.DoesNotContain("changes", html, StringComparison.OrdinalIgnoreCase);
    }

    // Pins the EnsureAsync contract that makes the out-of-band live-block correction
    // (quickstart §5) necessary: once a row exists, the stored Value renders and the view's
    // defaultHtml is ignored. Green before and after the copy edit.
    [Fact]
    public async Task APreviouslyProvisionedBlockRendersItsStoredValue_notTheDefault()
    {
        var html = await RenderNextStepsAsync(seedStoredValue: true);

        Assert.Contains("Hand-edited lead-in", html);
        Assert.Contains("custom wording an editor typed", html);
        Assert.DoesNotContain("the DfE will review", html);
    }

    private async Task<string> RenderNextStepsAsync(bool seedStoredValue)
    {
        // The integration database is shared and never reset between tests, so each test
        // starts from the same "no row for this key yet" state whatever ran before it.
        await ResetBlockAsync();

        await using var ctx = fixture.CreateContext();
        var contentBlockService = new ContentBlockService(
            new ContentBlockRepository(ctx), new HtmlRenderingService());

        if (seedStoredValue)
        {
            await contentBlockService.SaveAsync(new SaveContentBlockDto
            {
                Key = BlockKey,
                BlockType = "Content",
                Value = StoredValue,
            });
        }

        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(PageController).Assembly);
                    services.AddGovUkFrontend();
                    services.AddSingleton<ISearchSurfaceFilter, AllSearchSurfaces>();
                    services.AddSingleton<IContentBlockService>(contentBlockService);
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = sp,
            // Default.cshtml calls User.IsInRole; a bare identity renders the read-only branch.
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity()),
        };
        httpContext.Request.Path = "/Journey/Confirmation";

        var routeData = new RouteData();
        routeData.Values["controller"] = "Journey";
        routeData.Routers.Add(new RouteCollection());
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        // The component under test, with the real service — the same object graph the view
        // gets in the app (the view component is activated by type with this service injected).
        var component = new EditableContentViewComponent(
            sp.GetRequiredService<IContentBlockService>())
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext { HttpContext = httpContext },
            },
        };

        // Pass the default the view hands over — read from the view itself so this test tracks
        // the source edit instead of duplicating the copy here (a local copy would never turn
        // green when the view is corrected).
        var result = await component.InvokeAsync(BlockKey, DefaultHtmlFromConfirmationView());
        var viewResult = Assert.IsType<ViewViewComponentResult>(result);

        var view = viewEngine.GetView(
            executingFilePath: null,
            viewPath: "/Views/Shared/Components/EditableContent/Default.cshtml",
            isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate the EditableContent Default view. " +
            $"Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var tempData = new TempDataDictionary(httpContext, tempDataProvider);
        await using var writer = new StringWriter();
        var viewData = viewResult.ViewData
            ?? throw new InvalidOperationException(
                $"The EditableContent component returned no ViewData for {BlockKey}.");
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }

    private async Task ResetBlockAsync()
    {
        await using var ctx = fixture.CreateContext();
        var block = await ctx.ContentBlocks
            .Include(b => b.Versions)
            .FirstOrDefaultAsync(b => b.Key == BlockKey);
        if (block is null)
        {
            return;
        }

        ctx.ContentBlockVersions.RemoveRange(block.Versions);
        ctx.ContentBlocks.Remove(block);
        await ctx.SaveChangesAsync();
    }

    // The defaultHtml literal of the journey_confirmation_next_steps invocation in
    // Views/Journey/Confirmation.cshtml, unescaped back into the HTML it passes to the component.
    private static string DefaultHtmlFromConfirmationView()
    {
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web"));
        var source = File.ReadAllText(Path.Combine(viewsDir, "Views/Journey/Confirmation.cshtml"));

        var match = Regex.Match(
            source, "defaultHtml\\s*=\\s*\"(?<value>(?:[^\"\\\\]|\\\\.)*)\"");
        Assert.True(match.Success,
            "Confirmation.cshtml no longer passes a defaultHtml literal to EditableContent.");

        return match.Groups["value"].Value
            .Replace("\\\"", "\"")
            .Replace("\\n", "\n");
    }
}
