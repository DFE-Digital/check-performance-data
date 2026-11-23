using DfE.CheckPerformanceData.Application.Media.Folders;

namespace DfE.CheckPerformanceData.UnitTests.Media.Folders;

public sealed class MediaFolderTreeTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    // Photos (A) holds Schools (B), which holds Pupils (C). icons (D) is at the top.
    private static MediaFolderTree Sample() => new([
        new MediaFolderDto(C, "Pupils", B),
        new MediaFolderDto(A, "Photos", null),
        new MediaFolderDto(B, "Schools", A),
        new MediaFolderDto(D, "icons", null),
    ]);

    [Fact]
    public void Folders_SortByName_IgnoringCase()
    {
        Assert.Equal(["icons", "Photos"], Sample().ChildrenOf(null).Select(f => f.Name));
    }

    [Fact]
    public void Flatten_PutsEachFolderBeforeItsSubfolders_WithItsDepth()
    {
        var items = Sample().Flatten();

        Assert.Equal(["icons:0", "Photos:0", "Schools:1", "Pupils:2"], items.Select(i => $"{i.Folder.Name}:{i.Depth}"));
    }

    [Fact]
    public void PathOf_RunsFromTheTopDown()
    {
        var tree = Sample();

        Assert.Equal([A, B, C], tree.PathOf(C).Select(f => f.Id));
        Assert.Equal("Photos / Schools / Pupils", tree.PathText(C));
        Assert.Empty(tree.PathOf(null));
        Assert.Empty(tree.PathOf(Guid.NewGuid()));
    }

    [Fact]
    public void SubtreeOf_HoldsTheFolderAndEveryFolderInsideIt()
    {
        Assert.Equal(new[] { A, B, C }.Order(), Sample().SubtreeOf(A).Order());
        Assert.Equal([D], Sample().SubtreeOf(D));
        Assert.Empty(Sample().SubtreeOf(Guid.NewGuid()));
    }

    [Fact]
    public void IsSameOrInside_IsTrueForTheFolderAndItsDescendants_Only()
    {
        var tree = Sample();

        Assert.True(tree.IsSameOrInside(A, A));
        Assert.True(tree.IsSameOrInside(C, A));
        Assert.False(tree.IsSameOrInside(A, C));
        Assert.False(tree.IsSameOrInside(D, A));
    }

    [Fact]
    public void ACycleInBadData_DoesNotHang()
    {
        var tree = new MediaFolderTree([new MediaFolderDto(A, "One", B), new MediaFolderDto(B, "Two", A)]);

        Assert.Equal(2, tree.PathOf(A).Count);
        Assert.Empty(tree.Flatten());
    }
}
