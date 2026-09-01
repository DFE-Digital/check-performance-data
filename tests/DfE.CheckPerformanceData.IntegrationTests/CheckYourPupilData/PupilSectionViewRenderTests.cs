using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;
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

namespace DfE.CheckPerformanceData.IntegrationTests.CheckYourPupilData;

// Renders Views/CheckYourPupilData/_PupilSection.cshtml through the real Razor view engine.
//
// Regression guard for GitHub issue #269: searching for a non-existent pupil used to hide the
// search box (the whole else-branch, including _PupilSearch, was skipped once TotalPages hit
// zero), leaving only the misleading MOJ "No data to check" alert and no way back to the table.
//
// Rules under test:
//   - genuinely no data AND no active search -> the editable MOJ alert, and NO search box
//   - search that returned zero matches -> the search box stays visible, a GDS-style "no results"
//     message is shown instead of the MOJ alert, and the term is preserved so the user can refine
//     or clear it
//   - data present -> heading, download link, search box, table (unchanged)
public sealed class PupilSectionViewRenderTests
{
    private static readonly string WindowId = Guid.NewGuid().ToString();

    [Fact]
    public async Task NoData_NoSearch_RendersMojAlert_WithoutSearchBoxOrTable()
    {
        var section = Section(totalPages: 0, search: null);
        var html = await RenderSectionAsync(section);

        Assert.Contains("moj-alert", html);
        Assert.Contains("No data to check", html);
        Assert.DoesNotContain("pupil-search", html);
        Assert.DoesNotContain("govuk-table", html);
    }

    [Fact]
    public async Task NoData_WithSearch_RendersSearchBoxAndGdsMessage_InsteadOfMojAlert()
    {
        var section = Section(totalPages: 0, search: "tim");
        var html = await RenderSectionAsync(section);

        // The search box must stay usable so the user can refine or clear the search.
        Assert.DoesNotContain("moj-alert", html);
        Assert.Contains("pupil-search", html);
        Assert.Contains("Search for a pupil by first or last name", html);

        // The search term is echoed in the input AND quoted in the no-results message.
        Assert.Contains("value=\"tim\"", html);
        Assert.Contains("<strong>tim</strong>", html);
        Assert.Contains("No pupils found for", html);

        // Zero matches -> no table.
        Assert.DoesNotContain("govuk-table__body", html);

        // A clear affordance is available.
        Assert.Contains(">Clear<", html);
    }

    [Fact]
    public async Task HasData_NoSearch_RendersHeadingSearchBoxTableAndDownloadLink()
    {
        var section = Section(
            totalPages: 2,
            search: null,
            table: new PupilTable(
                ["Name", "URN"],
                [new[] { "Tim Smith", "123456" }, new[] { "Amelia Jones", "654321" }]));

        var html = await RenderSectionAsync(section);

        Assert.DoesNotContain("moj-alert", html);
        Assert.Contains("Pupil included", html);
        Assert.Contains("Download", html);
        Assert.Contains("as a CSV file", html);
        Assert.Contains("pupil-search", html);
        Assert.Contains("govuk-table__body", html);
        Assert.Contains("Tim Smith", html);
        Assert.Contains("Amelia Jones", html);
    }

    [Fact]
    public async Task HasData_WithSearch_PreservesTermAndShowsTable_NotMojAlert()
    {
        var section = Section(
            totalPages: 1,
            search: "amelia",
            table: new PupilTable(["Name"], [new[] { "Amelia Jones" }]));

        var html = await RenderSectionAsync(section);

        Assert.DoesNotContain("moj-alert", html);
        Assert.Contains("pupil-search", html);
        Assert.Contains("value=\"amelia\"", html);
        Assert.Contains(">Clear<", html);
        Assert.Contains("govuk-table__body", html);
        Assert.Contains("Amelia Jones", html);
    }

    private static PupilTableSection Section(
        int totalPages,
        string? search,
        PupilTable? table = null)
    {
        var section = new PupilTableSection
        {
            Key = "included",
            TabLabel = "Included",
            Heading = "Pupil included",
            DownloadAction = "DownloadIncluded",
            DownloadLinkText = "pupil included data",
            EmptyContentKey = "check-pupil-data-no-included-data-content",
            EmptyContentHtml = "<p>There's no pupil included data for your school to check in this window.</p>",
            Table = table ?? PupilTable.Empty,
            Page = 0,
            TotalPages = totalPages,
            Search = search,
            LearnerNoun = LearnerNoun.Pupil,
        };

        return section;
    }

    // Renders Views/CheckYourPupilData/_PupilSection.cshtml with isMainPage:false — skips the
    // _ViewStart layout so the test does not need the full page dependency graph. The MOJ alert
    // branch invokes the EditableContent view component, so a stubbed IContentBlockService is
    // registered to keep the component (and its auto-provisioning path) working.
    private static async Task<string> RenderSectionAsync(PupilTableSection section)
    {
        var contentBlockService = Substitute.For<IContentBlockService>();
        contentBlockService
            .EnsureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new ContentBlockDto
            {
                Key = section.EmptyContentKey,
                BlockType = "Content",
                Value = "",
                ValueHtml = section.EmptyContentHtml,
            });

        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddControllersWithViews()
                        .AddApplicationPart(typeof(CheckYourPupilDataController).Assembly);
                    // GDS tag helpers used by the view (govuk-button, govuk-pagination) need the
                    // ComponentGenerator service graph — mirror the production Program.cs
                    // registration so the same view code path runs under test.
                    services.AddGovUkFrontend();
                    services.AddScoped<IContentBlockService>(_ => contentBlockService);
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
        routeData.Values["controller"] = "CheckYourPupilData";
        // The download link uses asp-action/asp-controller tag helpers; give the UrlHelper a
        // (non-matching) router so it returns a link instead of throwing. href values aren't
        // asserted here — only the presence of the download affordance and surrounding copy.
        routeData.Routers.Add(new RouteCollection());
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());

        var view = viewEngine.GetView(
            executingFilePath: null,
            viewPath: "/Views/CheckYourPupilData/_PupilSection.cshtml",
            isMainPage: false);
        Assert.True(view.Success,
            $"Could not locate _PupilSection view. Searched: {string.Join(", ", view.SearchedLocations ?? [])}");

        var model = new PupilSectionViewModel
        {
            WindowId = WindowId,
            Section = section,
            AllSections = [section],
            TabAnchor = "included",
        };
        var viewData = new ViewDataDictionary<PupilSectionViewModel>(
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
