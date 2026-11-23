using DfE.CheckPerformanceData.Application.Media.Folders;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Media.Folders;

public sealed class MediaFolderServiceTests
{
    private static readonly Guid Photos = Guid.NewGuid();
    private static readonly Guid Schools = Guid.NewGuid();
    private static readonly Guid Pupils = Guid.NewGuid();

    private readonly IMediaFolderRepository _repo = Substitute.For<IMediaFolderRepository>();
    private readonly MediaFolderService _sut;

    public MediaFolderServiceTests()
    {
        _repo.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<MediaFolderDto>
        {
            new(Photos, "Photos", null),
            new(Schools, "Schools", Photos),
            new(Pupils, "Pupils", Schools),
        });
        foreach (var folder in new[] { Photos, Schools, Pupils })
        {
            var id = folder;
            _repo.GetAsync(id, Arg.Any<CancellationToken>()).Returns(ci => _repo.ListAsync().Result.Single(f => f.Id == id));
        }
        _sut = new MediaFolderService(_repo);
    }

    [Fact]
    public async Task Create_WithABadName_WritesNothing()
    {
        var result = await _sut.CreateAsync(" ", Photos);

        Assert.Equal(MediaFolderOutcome.NameInvalid, result.Outcome);
        Assert.Equal(MediaFolderNames.Missing, result.Error);
        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default);
    }

    [Fact]
    public async Task Create_AddsATrimmedFolder_InTheParent()
    {
        _repo.CreateAsync(Arg.Any<Guid>(), "Logos", Photos, Arg.Any<CancellationToken>()).Returns(MediaFolderOutcome.Done);

        var result = await _sut.CreateAsync("  Logos ", Photos);

        Assert.True(result.Succeeded);
        Assert.Equal("Logos", result.Folder!.Name);
        Assert.Equal(Photos, result.Folder.ParentId);
        await _repo.Received(1).CreateAsync(result.Folder.Id, "Logos", Photos, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WhenTheNameIsTaken_SaysSo()
    {
        _repo.CreateAsync(Arg.Any<Guid>(), "schools", Photos, Arg.Any<CancellationToken>()).Returns(MediaFolderOutcome.NameTaken);

        var result = await _sut.CreateAsync("schools", Photos);

        Assert.Equal(MediaFolderOutcome.NameTaken, result.Outcome);
        Assert.Equal(MediaFolderNames.Taken("schools"), result.Error);
    }

    [Fact]
    public async Task Create_InAFolderThatIsGone_IsRefused()
    {
        var result = await _sut.CreateAsync("Logos", Guid.NewGuid());

        Assert.Equal(MediaFolderOutcome.ParentNotFound, result.Outcome);
        await _repo.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Move_IntoItselfOrAnyFolderInsideIt_IsRefused_BeforeAnyWrite(int target)
    {
        var into = new[] { Photos, Schools, Pupils }[target];

        var result = await _sut.MoveAsync(Photos, into);

        Assert.Equal(MediaFolderOutcome.Cycle, result.Outcome);
        Assert.Equal(MediaFolderService.CycleError, result.Error);
        await _repo.DidNotReceiveWithAnyArgs().MoveAsync(default, default);
    }

    [Fact]
    public async Task Move_ToTheTopOrAnotherBranch_IsWritten()
    {
        _repo.MoveAsync(Pupils, null, Arg.Any<CancellationToken>()).Returns(MediaFolderOutcome.Done);

        var result = await _sut.MoveAsync(Pupils, null);

        Assert.True(result.Succeeded);
        await _repo.Received(1).MoveAsync(Pupils, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Move_ThatTheDatabaseFindsIsACycle_IsRefused()
    {
        // Another editor moved a folder at the same time, so only the database saw the loop.
        _repo.MoveAsync(Schools, null, Arg.Any<CancellationToken>()).Returns(MediaFolderOutcome.Cycle);

        var result = await _sut.MoveAsync(Schools, null);

        Assert.Equal(MediaFolderOutcome.Cycle, result.Outcome);
        Assert.Equal(MediaFolderService.CycleError, result.Error);
    }

    [Fact]
    public async Task Delete_AFolderThatHoldsThings_SaysWhat_AndDeletesNothing()
    {
        _repo.CountContentsAsync(Schools, Arg.Any<CancellationToken>()).Returns((2, 1));

        var result = await _sut.DeleteAsync(Schools);

        Assert.Equal(MediaFolderOutcome.NotEmpty, result.Outcome);
        Assert.Equal("You cannot delete ‘Schools’ because it holds 2 images and 1 folder. Move or delete them first.", result.Error);
        await _repo.DidNotReceiveWithAnyArgs().DeleteAsync(default);
    }

    [Fact]
    public async Task Delete_AnEmptyFolder_DeletesIt()
    {
        _repo.CountContentsAsync(Pupils, Arg.Any<CancellationToken>()).Returns((0, 0));
        _repo.DeleteAsync(Pupils, Arg.Any<CancellationToken>()).Returns(MediaFolderOutcome.Done);

        var result = await _sut.DeleteAsync(Pupils);

        Assert.True(result.Succeeded);
        Assert.Equal("Pupils", result.Folder!.Name);
    }

    [Fact]
    public async Task Delete_AnUnknownFolder_IsNotFound()
    {
        var result = await _sut.DeleteAsync(Guid.NewGuid());

        Assert.Equal(MediaFolderOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task MoveImages_WithNoneChosen_IsRefused()
    {
        var result = await _sut.MoveImagesAsync([], Photos);

        Assert.False(result.Succeeded);
        Assert.Equal(MediaFolderService.NoImagesError, result.Error);
    }

    [Fact]
    public async Task MoveImages_IntoAFolderThatIsGone_IsRefused()
    {
        _repo.MoveImagesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Photos, Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await _sut.MoveImagesAsync([Guid.NewGuid()], Photos);

        Assert.Equal(MediaFolderOutcome.NotFound, result.Outcome);
        Assert.Equal(MediaFolderService.FolderGoneError, result.Error);
    }

    [Fact]
    public async Task MoveImages_AnswersHowManyMoved()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _repo.MoveImagesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), null, Arg.Any<CancellationToken>()).Returns(2);

        var result = await _sut.MoveImagesAsync(ids, null);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Moved);
    }
}
