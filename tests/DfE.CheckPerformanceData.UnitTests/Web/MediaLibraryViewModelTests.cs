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

    private static MediaLibraryIndexViewModel Model(string? search = null, bool missingAlt = false, bool unused = false, string sort = "newest",
        string view = MediaLibraryView.Grid) => new()
    {
        Page = new MediaAssetPage([], 0, 1, 20),
        Items = [],
        Search = search,
        MissingAlt = missingAlt,
        Unused = unused,
        Sort = sort,
        View = view,
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

    [Fact]
    public void PageUrl_KeepsTheView_UnlessItIsTheGrid()
    {
        Assert.Equal("/admin/media?search=chart&view=details&page=2", Model("chart", view: MediaLibraryView.Details).PageUrl(2));
        Assert.Equal("/admin/media?view=large", Model(view: MediaLibraryView.Large).PageUrl(1));
        Assert.Equal("/admin/media?search=chart", Model("chart").PageUrl(1));
    }

    [Fact]
    public void ClearFiltersUrl_KeepsOnlyTheView()
    {
        Assert.Equal("/admin/media?view=details", Model("chart", missingAlt: true, sort: "name", view: MediaLibraryView.Details).ClearFiltersUrl);
        Assert.Equal("/admin/media", Model("chart", unused: true).ClearFiltersUrl);
    }

    [Theory]
    [InlineData(null, "grid")]
    [InlineData("", "grid")]
    [InlineData("bogus", "grid")]
    [InlineData("grid", "grid")]
    [InlineData("details", "details")]
    [InlineData("DETAILS", "details")]
    [InlineData("large", "large")]
    public void AView_IsOneOfTheThree_AndAnythingElseIsTheGrid(string? asked, string expected)
    {
        Assert.Equal(expected, MediaLibraryView.Normalise(asked));
    }

    [Theory]
    [InlineData("image/png", "PNG")]
    [InlineData("image/jpeg", "JPEG")]
    [InlineData("image/gif", "GIF")]
    [InlineData("image/webp", "WebP")]
    [InlineData("image/svg+xml", "SVG")]
    [InlineData("image/x-unknown", "image/x-unknown")]
    public void TypeName_IsTheFormatsShortName(string contentType, string expected)
    {
        var item = new MediaLibraryItemViewModel(Guid.NewGuid(), "a", "/t", "/o", "/d", null, "1 KB", contentType, null, 1, false, DateTime.UtcNow);

        Assert.Equal(expected, item.TypeName);
    }

    [Fact]
    public void TheViews_AreGridDetailsAndLarge_InThatOrder()
    {
        Assert.Equal(["grid", "details", "large"], MediaLibraryView.All.Select(v => v.Value));
        Assert.Equal(["Grid", "Details", "Large"], MediaLibraryView.All.Select(v => v.Label));
    }
}
