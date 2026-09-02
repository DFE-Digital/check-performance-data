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

// Renders Views/Journey/_Checkbox.cshtml through the real Razor view engine, the same way
// FreeTextRenderTests does for the single-line input. The accessibility rules this pins are the
// ones docs/accessibility.md records as defects found once: exactly one <h1> per page (the
// legend carries the heading, never a second one), and an aria-describedby that names only
// elements the view actually emits.
public sealed class CheckboxRenderTests
{
    private static Question YearsQuestion(string? hint = null) => new()
    {
        Id = "years-to-remove",
        Type = QuestionType.Checkbox,
        Title = "Which years do you want to remove Billy B from?",
        Hint = hint,
        Options =
        [
            new QuestionOption { Value = "2025-2026", Label = "2025 to 2026" },
            new QuestionOption { Value = "2024-2025", Label = "2024 to 2025" },
            new QuestionOption { Value = "2023-2024", Label = "2023 to 2024" }
        ]
    };

    [Fact]
    public async Task EveryVisibleOption_RendersACheckboxSharingTheQuestionsFormKey()
    {
        var html = await RenderAsync(YearsQuestion());

        Assert.Contains("type=\"checkbox\"", html);
        Assert.Equal(3, html.Split("type=\"checkbox\"").Length - 1);
        Assert.Equal(3, html.Split("name=\"q_years_to_remove\"").Length - 1);
        Assert.Contains("value=\"2024-2025\"", html);
        Assert.Contains("2024 to 2025", html);
    }

    [Fact]
    public async Task EveryCheckboxHasItsOwnLabelPointingAtItsOwnId()
    {
        var html = await RenderAsync(YearsQuestion());

        Assert.Contains("id=\"q_years_to_remove-2024-2025\"", html);
        Assert.Contains("for=\"q_years_to_remove-2024-2025\"", html);
    }

    [Fact]
    public async Task PreviouslyTickedBoxesComeBackChecked()
    {
        var html = await RenderAsync(YearsQuestion(),
            new QuestionAnswer { SelectedValues = ["2025-2026", "2023-2024"] });

        Assert.Contains("value=\"2025-2026\" checked", html);
        Assert.Contains("value=\"2023-2024\" checked", html);
        Assert.DoesNotContain("value=\"2024-2025\" checked", html);
    }

    // The page heading is the legend. A separate <h1> would give the page two.
    [Fact]
    public async Task AsThePageHeading_TheLegendCarriesTheOnlyH1()
    {
        var html = await RenderAsync(YearsQuestion(), isPageHeading: true);

        Assert.Equal(1, html.Split("<h1").Length - 1);
        Assert.Contains("govuk-fieldset__legend--l", html);
    }

    // A dangling aria-describedby resolves to nothing and silently drops the whole description,
    // so the fieldset may only name ids the view emits.
    [Fact]
    public async Task WithNoHintAndNoError_TheFieldsetHasNoAriaDescribedby()
    {
        var html = await RenderAsync(YearsQuestion());

        Assert.DoesNotContain("aria-describedby", html);
    }

    [Fact]
    public async Task WithAHintAndAnError_TheFieldsetNamesBothRenderedIds()
    {
        var html = await RenderAsync(YearsQuestion(hint: "Select all that apply"), error: "Select a year");

        Assert.Contains("id=\"q_years_to_remove-hint\"", html);
        Assert.Contains("id=\"q_years_to_remove-error\"", html);
        Assert.Contains("aria-describedby=\"q_years_to_remove-hint q_years_to_remove-error\"", html);
    }

    private static async Task<string> RenderAsync(
        Question question, QuestionAnswer? existingAnswer = null,
        bool isPageHeading = false, string? error = null)
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
        var routeData = new RouteData();
        routeData.Values["controller"] = "Journey";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.FindView(actionContext, "_Checkbox", isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate _Checkbox view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var model = new QuestionPartialModel
        {
            WindowId = Guid.NewGuid(),
            PageId = "years-to-remove",
            Question = question,
            ExistingAnswer = existingAnswer,
            IsPageHeading = isPageHeading,
            Error = error,
            ResolvedTitle = question.Title,
            VisibleOptions = question.Options ?? []
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
