using DfE.CheckPerformanceData.Application.Wiki;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Bodies filled by Plan 07 Task 4 — Index_RendersSearchBox asserts the _WikiSearch
// partial is wired into the Help/Index sidebar (ViewResult + Model inspection; no
// Razor render harness needed — the assertion is on controller output shape).
public sealed class HelpControllerTests
{
    private readonly IWikiService _wikiService = Substitute.For<IWikiService>();
    // HelpController ctor may have additional deps — Plan 07 Task 4 mirrors them.

    [Fact]
    public async Task Index_RendersSearchBox()
    {
        await Task.CompletedTask; // TODO(Plan 07 Task 4): assert Help/Index wires the _WikiSearch partial
    }
}
