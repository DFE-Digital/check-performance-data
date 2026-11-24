using DfE.CheckPerformanceData.Application.Media.Folders;
using DfE.CheckPerformanceData.Web.Admin.Nav;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// The admin nav shows the media library's folders under its "Media library" node, like the page
// tree under Pages. Each folder opens the library in that folder, and a folder lights up only on
// its own address.
public sealed class AdminNavMediaFolderTests
{
    private static readonly Guid Photos = Guid.NewGuid();
    private static readonly Guid Schools = Guid.NewGuid();
    private static readonly Guid Icons = Guid.NewGuid();

    private static MediaFolderTree Tree() => new([
        new MediaFolderDto(Photos, "Photos", null),
        new MediaFolderDto(Schools, "Schools", Photos),
        new MediaFolderDto(Icons, "Icons", null),
    ]);

    private static IReadOnlyList<AdminNavNodeViewModel> Forest() => AdminNavNodeViewModel.BuildForest(
    [
        new CmsAdminGroupNavEntry(),
        new MediaLibraryNavEntry(),
        new ContentPagesNavEntry(),
    ]);

    private static AdminNavNodeViewModel Media(IReadOnlyList<AdminNavNodeViewModel> forest) =>
        AdminNavNodeViewModel.CollectNodes(forest).Single(n => n.Entry.Key == AdminNavKeys.Media);

    [Fact]
    public void TheFolderTree_IsGraftedUnderTheMediaLibrary_InNameOrder()
    {
        var media = Media(AdminNavNodeViewModel.GraftMediaFolders(Forest(), Tree()));

        Assert.Equal(["Icons", "Photos"], media.Children.Select(c => c.Entry.Title));
        var photos = media.Children[1];
        Assert.Equal($"/admin/media?folder={Photos:D}", photos.Entry.Url);
        Assert.Equal(["Schools"], photos.Children.Select(c => c.Entry.Title));
        Assert.Equal($"/admin/media?folder={Schools:D}", photos.Children[0].Entry.Url);
    }

    [Fact]
    public void NoFolders_LeavesTheForestAsItWas()
    {
        var forest = Forest();

        Assert.Same(forest, AdminNavNodeViewModel.GraftMediaFolders(forest, MediaFolderTree.Empty));
    }

    [Fact]
    public void AFolder_IsTheActiveEntry_OnlyOnItsOwnAddress()
    {
        var entries = AdminNavNodeViewModel.CollectEntries(AdminNavNodeViewModel.GraftMediaFolders(Forest(), Tree())).ToList();

        Assert.Equal($"media-folder-{Schools:D}", AdminNavActive.ResolveActiveKey("/admin/media", $"?folder={Schools:D}&subfolders=false", entries));
        Assert.Equal(AdminNavKeys.Media, AdminNavActive.ResolveActiveKey("/admin/media", "", entries));
        // The ticked "Include subfolders" box sends the key twice.
        Assert.Equal($"media-folder-{Schools:D}", AdminNavActive.ResolveActiveKey("/admin/media", $"?folder={Schools:D}&subfolders=true&subfolders=false", entries));
        Assert.Equal(AdminNavKeys.Media, AdminNavActive.ResolveActiveKey($"/admin/media/{Guid.NewGuid()}", "", entries));
    }

    // The folders follow the Media section: a user without it sees neither the node nor its folders.
    [Fact]
    public void AUserWithoutTheMediaSection_SeesNoFolders()
    {
        var grafted = AdminNavNodeViewModel.GraftMediaFolders(Forest(), Tree());

        var filtered = AdminNavNodeViewModel.FilterByAccess(grafted, key => key != AdminNavKeys.Media);

        var all = AdminNavNodeViewModel.CollectEntries(filtered).ToList();
        Assert.DoesNotContain(all, e => e.Key == AdminNavKeys.Media);
        Assert.DoesNotContain(all, e => e is MediaFolderNavEntry);
        Assert.Contains(all, e => e.Key == AdminNavKeys.ContentPages);
    }

    [Fact]
    public void AUserWithTheMediaSection_SeesTheFolders()
    {
        var grafted = AdminNavNodeViewModel.GraftMediaFolders(Forest(), Tree());

        var filtered = AdminNavNodeViewModel.FilterByAccess(grafted, key => key == AdminNavKeys.Media);

        Assert.Equal(3, AdminNavNodeViewModel.CollectEntries(filtered).OfType<MediaFolderNavEntry>().Count());
    }
}
