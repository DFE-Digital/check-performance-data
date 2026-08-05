using DfE.CheckPerformanceData.Web.Common;

namespace DfE.CheckPerformanceData.Application.UnitTests.Common;

public class PageTitleTests
{
    [Fact]
    public void WithErrorPrefix_WhenNoError_LeavesTitleAlone()
    {
        Assert.Equal("Check your answers", PageTitle.WithErrorPrefix("Check your answers", hasError: false));
    }

    [Fact]
    public void WithErrorPrefix_WhenError_PrefixesTitle()
    {
        Assert.Equal("Error: Check your answers", PageTitle.WithErrorPrefix("Check your answers", hasError: true));
    }

    [Fact]
    public void WithErrorPrefix_IsIdempotent()
    {
        // The layout applies this once, but a view is free to have prefixed its own title.
        // Applying it twice must not produce "Error: Error: ...".
        var once = PageTitle.WithErrorPrefix("Check your answers", hasError: true);
        Assert.Equal("Error: Check your answers", PageTitle.WithErrorPrefix(once, hasError: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithErrorPrefix_WithNoTitle_DoesNotProduceABareErrorLabel(string? title)
    {
        // "Error:" on its own names no page, so there is nothing useful to prefix.
        Assert.Equal(title, PageTitle.WithErrorPrefix(title, hasError: true));
    }
}
