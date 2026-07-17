using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.AmendmentRequests;
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

namespace DfE.CheckPerformanceData.IntegrationTests.AmendmentRequests;

// Renders Views/Shared/_SummaryDetails.cshtml through the real Razor view engine to prove the
// shared partial compiles and that ShowChangeLinks=false (the bulk detailed-review page)
// suppresses every "Change" link while still rendering the answers and uploaded-file rows.
// The ShowChangeLinks=true path is the unchanged journey-Summary markup; its "Change" anchors use
// asp-action tag helpers that need full endpoint routing to generate URLs, so they are not
// exercised here (they are covered by the app itself).
public sealed class SummaryDetailsRenderTests
{
    [Fact]
    public async Task WithoutChangeLinks_RendersAnswersAndFilesButNoChangeLinks()
    {
        var html = await RenderAsync("_SummaryDetails", SampleModel(),
            viewData: new Dictionary<string, object?> { ["ShowChangeLinks"] = false });

        Assert.Contains("What pupil data would you like to change?", html);
        Assert.Contains("Ann Alpha", html);            // pupil name row
        Assert.Contains("Left the school", html);      // question answer
        Assert.DoesNotContain("Change", html);         // no change links
    }

    [Fact]
    public async Task DuplicatesWarning_RendersReasonRows()
    {
        IReadOnlyList<BulkReviewItemViewModel> duplicates =
        [
            new BulkReviewItemViewModel
            {
                ReferenceNumber = "DUP1",
                PupilName = "Bob Beta",
                RequestTypeDescription = "Remove pupil",
                DuplicateReason = "A request for this pupil has already been submitted."
            }
        ];

        var html = await RenderAsync("_BulkDuplicatesWarning", duplicates, viewData: null);

        Assert.Contains("Bob Beta", html);
        Assert.Contains("will not be submitted", html);
        Assert.Contains("already been submitted", html);
    }

    private static SummaryViewModel SampleModel()
    {
        var page = new JourneyPage { Id = "reason" };
        var question = new Question { Id = "reason", Type = QuestionType.FreeText, Title = "Why?" };
        var answer = new QuestionAnswer { TextValue = "Left the school" };
        return new SummaryViewModel
        {
            WindowId = Guid.NewGuid(),
            WhatToChange = WhatToChange.Remove,
            PupilName = "Ann Alpha",
            PrimaryPupilPageId = "select-pupil",
            Rows = [new SummaryRow(page, question, answer, "Reason")],
            FileRows = [],
            BackPageId = "reason",
            MaxEvidencePages = 6
        };
    }

    private static async Task<string> RenderAsync<TModel>(
        string viewName, TModel model, IReadOnlyDictionary<string, object?>? viewData)
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
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        var view = viewEngine.FindView(actionContext, viewName, isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate {viewName} view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var dictionary = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        if (viewData is not null)
            foreach (var (key, value) in viewData)
                dictionary[key] = value;

        var tempData = new TempDataDictionary(httpContext, tempDataProvider);

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext, view.View, dictionary, tempData, writer, new HtmlHelperOptions());
        await view.View.RenderAsync(viewContext);

        return writer.ToString();
    }
}
