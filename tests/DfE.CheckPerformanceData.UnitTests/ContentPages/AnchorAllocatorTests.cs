using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// Heading widgets get a server-generated anchor (the <section id> + nav target). Anchors derive
// from the heading text but must be unique within a page, so duplicates get a numeric suffix.
public class AnchorAllocatorTests
{
    [Fact]
    public void Allocate_SlugifiesHeadingText()
    {
        var sut = new AnchorAllocator();

        Assert.Equal("key-dates", sut.Allocate("Key dates"));
    }

    [Fact]
    public void Allocate_StripsPunctuation_LikeWikiSlug()
    {
        var sut = new AnchorAllocator();

        Assert.Equal("child-missing-education", sut.Allocate("Child missing education!"));
    }

    [Fact]
    public void Allocate_DeduplicatesWithNumericSuffix()
    {
        var sut = new AnchorAllocator();

        Assert.Equal("key-dates", sut.Allocate("Key dates"));
        Assert.Equal("key-dates-2", sut.Allocate("Key dates"));
        Assert.Equal("key-dates-3", sut.Allocate("Key dates"));
    }

    [Fact]
    public void Allocate_BlankOrSymbolOnlyText_FallsBackToSection()
    {
        var sut = new AnchorAllocator();

        Assert.Equal("section", sut.Allocate("   "));
        Assert.Equal("section-2", sut.Allocate("!!!"));
    }
}
