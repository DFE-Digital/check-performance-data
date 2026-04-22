using DfE.CheckPerformanceData.Application.Wiki;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Wiki;

public sealed class HelpControllerSearchTests
{
    private readonly IWikiService _wikiService = Substitute.For<IWikiService>();
    // HelpController _sut will be constructed in each test — controller may have other ctor deps; Plan 06 wires this up.

    // --- Search action — validation ---

    [Fact]
    public async Task Search_EmptyQuery_RendersErrorSummary()
    {
        await Task.CompletedTask; // TODO(Plan 06): assert error-summary rendering for empty q
    }

    [Fact]
    public async Task Search_RendersWithQueryPreFilled()
    {
        await Task.CompletedTask; // TODO(Plan 06): assert VM.CurrentQuery echoed
    }

    [Fact]
    public async Task Search_ZeroResults_RendersEmptyState()
    {
        await Task.CompletedTask; // TODO(Plan 06): assert zero-results copy + link to /help
    }

    // --- Pagination ---

    [Fact]
    public async Task Search_OutOfRangePage_Clamps()
    {
        await Task.CompletedTask; // TODO(Plan 06): assert clamp logic
    }
}
