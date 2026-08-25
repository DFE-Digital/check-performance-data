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

// Renders Views/Journey/_FreeText.cshtml through the real Razor view engine, the same way
// SummaryDetailsRenderTests does for the shared summary partial. A declared
// characterLimit was enforced on the server only, so the browser let the user type well past a cap
// their submit would then be rejected for. The GOV.UK character count component is textarea-only,
// so the single-line input carries maxlength instead.
public sealed class FreeTextRenderTests
{
    [Fact]
    public async Task WhenTheQuestionDeclaresACharacterLimit_TheInputCarriesMaxlength()
    {
        var html = await RenderAsync(new Question
        {
            Id = "upn",
            Type = QuestionType.FreeText,
            Title = "What is the pupil's UPN?",
            CharacterLimit = 13
        });

        Assert.Contains("maxlength=\"13\"", html);
    }

    [Fact]
    public async Task WhenTheQuestionDeclaresNoCharacterLimit_TheInputHasNoMaxlength()
    {
        var html = await RenderAsync(new Question
        {
            Id = "dfe-number",
            Type = QuestionType.FreeText,
            Title = "What is the DfE number?"
        });

        Assert.DoesNotContain("maxlength", html);
    }

    private static async Task<string> RenderAsync(Question question)
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
                });
                web.Configure(_ => { });
            })
            .StartAsync();

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var viewEngine = sp.GetRequiredService<ICompositeViewEngine>();
        var tempDataProvider = sp.GetRequiredService<ITempDataProvider>();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        // The partial lives in Views/Journey rather than Views/Shared, so the view engine needs
        // the controller name to find it.
        var routeData = new RouteData();
        routeData.Values["controller"] = "Journey";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.FindView(actionContext, "_FreeText", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate _FreeText view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var model = new QuestionPartialModel
        {
            WindowId = Guid.NewGuid(),
            PageId = "learner-details",
            Question = question,
            ResolvedTitle = question.Title
        };

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
