using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.Journey;
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

namespace DfE.CheckPerformanceData.IntegrationTests.Journey;

// Issue #634 / AB#309468: the evidence page labels are built by JourneyViewModelBuilder. When
// the English-not-first-language auto-rejection rule makes both fields optional, the builder
// appends " (Optional)". These render the real file-upload and text-area partials through the Razor
// view engine, proving the suffix reaches the visible labels rather than only the view model.
public sealed class EvidenceUploadRenderTests
{
    [Fact]
    public async Task ConditionallyOptionalEvidence_RendersTheOptionalSuffixOnBothLabels()
    {
        var fileHtml = await RenderAsync("_FileUpload", new QuestionPartialModel
        {
            PageId = "evidence",
            IsPageHeading = true,
            Question = new Question
            {
                Id = "evidence",
                Type = QuestionType.FileUpload,
                Title = "Upload files"
            },
            ResolvedTitle = "Upload files (Optional)"
        });

        var explanationHtml = await RenderAsync("_TextArea", new QuestionPartialModel
        {
            PageId = "evidence",
            IsPageHeading = true,
            Question = new Question
            {
                Id = "how-evidence-supports",
                Type = QuestionType.TextArea,
                Title = "How does the evidence demonstrate why the pupil should be removed?"
            },
            ResolvedTitle =
                "How does the evidence demonstrate why the pupil should be removed? (Optional)"
        });

        Assert.Contains("Upload files (Optional)", fileHtml);
        Assert.Contains(
            "How does the evidence demonstrate why the pupil should be removed? (Optional)",
            explanationHtml);
    }

    private static async Task<string> RenderAsync(string viewName, QuestionPartialModel model)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(PageController).Assembly);
                    services.AddRouting();
                    services.AddGovUkFrontend();
                    // The shared analytics filter partial in the form @inject-s ISearchSurfaceFilter.
                    services.AddSingleton<
                        DfE.CheckPerformanceData.Application.Analytics.ISearchSurfaceFilter,
                        DfE.CheckPerformanceData.Application.Analytics.AllSearchSurfaces>();
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        // With no endpoint set, UrlHelper falls back to the legacy IRouter (absent here) and every
        // asp-action tag helper throws. A placeholder endpoint selects endpoint-routing link
        // generation, which is what the real app uses.
        httpContext.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "test"));
        var routeData = new RouteData();
        routeData.Values["controller"] = "Journey";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.FindView(actionContext, viewName, isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate {viewName} view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var dictionary = new ViewDataDictionary<QuestionPartialModel>(
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
}
