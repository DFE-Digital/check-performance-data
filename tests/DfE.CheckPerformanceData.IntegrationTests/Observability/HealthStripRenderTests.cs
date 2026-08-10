using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.Observability;
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

namespace DfE.CheckPerformanceData.IntegrationTests.Observability;

// Renders the dashboard health strip partial through the real Razor view engine so the "why"
// disclosure is exercised end to end: a non-flowing light must surface a GDS "Why?" disclosure
// naming each signal that crossed a threshold and the actual-vs-limit figures, while a flowing
// light surfaces none. The Explain logic itself is unit-tested; this proves the model the
// controller assembles renders into the markup a stakeholder actually sees.
public sealed class HealthStripRenderTests
{
    private static HealthState NeedsAttention =>
        new(HealthLevel.NeedsAttention, "#d4351c", "Needs attention", "ring");

    private static HealthState Flowing =>
        new(HealthLevel.Flowing, "#00703c", "Flowing", "circle");

    [Fact]
    public async Task HealthStrip_NonFlowingLight_RendersWhyDisclosureWithFiguresAndBand()
    {
        var model = new DashboardViewModel
        {
            OverallHealth = NeedsAttention,
            OverallReasons = new[]
            {
                new HealthReason("Queue depth", HealthLevel.NeedsAttention, "80", "50"),
            },
            QueueHealth = new[]
            {
                new QueueHealth("rules", "Rules engine queue", NeedsAttention, new[]
                {
                    new HealthReason("Oldest message age", HealthLevel.BackingUp, "2m", "1m"),
                }),
            },
            StatusSentence = "Something needs attention.",
        };

        var html = await RenderHealthStripAsync(model);

        Assert.Contains("Why?", html);
        // The overall light explains itself with the depth figures and the attention band phrase.
        Assert.Contains("Queue depth", html);
        Assert.Contains("80", html);
        Assert.Contains("needs attention above", html);
        Assert.Contains("50", html);
        // The per-queue light explains itself with the age figures and the backing-up band phrase.
        Assert.Contains("Oldest message age", html);
        Assert.Contains("2m", html);
        Assert.Contains("backing up above", html);
    }

    [Fact]
    public async Task HealthStrip_AllFlowing_RendersNoWhyDisclosure()
    {
        var model = new DashboardViewModel
        {
            OverallHealth = Flowing,
            OverallReasons = Array.Empty<HealthReason>(),
            QueueHealth = new[]
            {
                new QueueHealth("rules", "Rules engine queue", Flowing, Array.Empty<HealthReason>()),
            },
            StatusSentence = "All systems healthy.",
        };

        var html = await RenderHealthStripAsync(model);

        Assert.DoesNotContain("Why?", html);
    }

    [Fact]
    public async Task HealthStrip_RendersOneWhyDisclosurePerLightThatHasReasons()
    {
        var model = new DashboardViewModel
        {
            OverallHealth = NeedsAttention,
            OverallReasons = new[]
            {
                new HealthReason("Dead-letter messages", HealthLevel.NeedsAttention, "9", "5"),
            },
            QueueHealth = new[]
            {
                // One queue tripped (has reasons), one flowing (none): exactly two disclosures —
                // the overall and the single tripped queue.
                new QueueHealth("rules", "Rules engine queue", NeedsAttention, new[]
                {
                    new HealthReason("Dead-letter messages", HealthLevel.NeedsAttention, "9", "5"),
                }),
                new QueueHealth("zendesk", "Zendesk queue", Flowing, Array.Empty<HealthReason>()),
            },
            StatusSentence = "Something needs attention.",
        };

        var html = await RenderHealthStripAsync(model);

        var occurrences = html.Split("Why?").Length - 1;
        Assert.Equal(2, occurrences);
    }

    // Spins up a minimal web host purely to obtain a fully wired MVC view engine, then renders the
    // Observability/_HealthStrip partial (which in turn renders _HealthReasons) to a string. The
    // controller route value scopes view discovery to the Observability folder so both partials
    // resolve from the compiled Web assembly.
    private static async Task<string> RenderHealthStripAsync(DashboardViewModel model)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(ObservabilityController).Assembly);
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var routeData = new RouteData();
        routeData.Values["controller"] = "Observability";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.FindView(actionContext, "_HealthStrip", isMainPage: false);
        Assert.True(view.Success, $"Could not locate _HealthStrip partial. Searched: {string.Join(", ", view.SearchedLocations ?? Array.Empty<string>())}");

        var viewData = new ViewDataDictionary<DashboardViewModel>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model,
        };
        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, viewData, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
