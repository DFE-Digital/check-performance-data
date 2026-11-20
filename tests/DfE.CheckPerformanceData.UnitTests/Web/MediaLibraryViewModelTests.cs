using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class MediaLibraryViewModelTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(1023, "1023 bytes")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(20L * 1024 * 1024, "20 MB")]
    [InlineData(5_500_000, "5.2 MB")]
    public void FileSize_IsReadable(long bytes, string expected)
    {
        Assert.Equal(expected, MediaFileSize.Format(bytes));
    }

    private static MediaLibraryIndexViewModel Model(string? search = null, bool missingAlt = false, bool unused = false, string sort = "newest") => new()
    {
        Page = new MediaAssetPage([], 0, 1, 20),
        Items = [],
        Search = search,
        MissingAlt = missingAlt,
        Unused = unused,
        Sort = sort,
    };

    [Fact]
    public void PageUrl_KeepsTheSearchFiltersAndSort()
    {
        var url = Model("bar chart & co", missingAlt: true, unused: true, sort: "name").PageUrl(3);

        Assert.Equal("/admin/media?search=bar%20chart%20%26%20co&missingAlt=true&unused=true&sort=name&page=3", url);
    }

    [Fact]
    public void PageUrl_ForTheFirstPage_OfTheDefaultList_HasNoQuery()
    {
        Assert.Equal("/admin/media", Model().PageUrl(1));
    }
}
