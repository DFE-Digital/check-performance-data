using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Bodies filled by Plan 07 Task 4 — Index_RendersSearchBox asserts the _WikiSearch
// partial is wired into the Help/Index sidebar. The assertion mixes controller-output
// shape (ViewResult) with Razor-source content (the _WikiSearch partial invocation in
// Index.cshtml and the govuk-input search contract in _WikiSearch.cshtml). No Razor
// render harness is introduced — source-content assertions are sufficient to pin the
// static Razor facts required by SEARCH-01.
public sealed class HelpControllerTests
{
    private readonly IWikiService _wikiService = Substitute.For<IWikiService>();

    [Fact]
    public async Task Index_RendersSearchBox()
    {
        // (a) Controller wiring — Index returns a ViewResult. HelpController.IsEditMode
        //     reads Request.Query, so the test provides a DefaultHttpContext via
        //     ControllerContext (standard ASP.NET Core unit-test pattern).
        _wikiService.GetNavigationTreeAsync().Returns(new List<WikiPageTreeNodeDto>());
        var sut = new HelpController(_wikiService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await sut.Index(slugPath: null);

        Assert.IsType<ViewResult>(result);

        // (b) Source-file assertion: Index.cshtml hosts the _WikiSearch partial.
        var viewsDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "DfE.CheckPerformanceData.Web", "Views", "Help"));
        var indexView = File.ReadAllText(Path.Combine(viewsDir, "Index.cshtml"));
        Assert.Contains("_WikiSearch", indexView);
        Assert.Contains("PartialAsync", indexView);

        // (c) Source-file assertion: _WikiSearch.cshtml has the govuk-input search contract.
        var partial = File.ReadAllText(Path.Combine(viewsDir, "_WikiSearch.cshtml"));
        Assert.Contains("<govuk-input", partial);
        Assert.Contains("type=\"search\"", partial);
        Assert.Contains("name=\"q\"", partial);
    }
}
