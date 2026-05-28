using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.Wiki;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Wiki;

public sealed class WikiServiceHardDeleteTests
{
    private readonly IWikiRepository _repository = Substitute.For<IWikiRepository>();
    private readonly IHtmlRenderingService _htmlRenderer = Substitute.For<IHtmlRenderingService>();
    private readonly WikiService _sut;

    public WikiServiceHardDeleteTests()
    {
        _sut = new WikiService(_repository, _htmlRenderer);

        // Pass-through transaction substitute — runs the supplied work delegate inline.
        _repository.ExecuteInTransactionAsync(Arg.Any<Func<Task>>())
            .Returns(ci => ((Func<Task>)ci[0])());
    }

    // --- HardDeletePageAsync_ThrowsInvalidOperation_WhenPageNotSoftDeleted ---

    [Fact]
    public async Task HardDeletePageAsync_ThrowsInvalidOperation_WhenPageNotSoftDeleted()
    {
        _repository.GetByIdIncludingDeletedAsync(5)
            .Returns(new WikiPageDto { Id = 5, Title = "Live page", Slug = "live-page" });
        _repository.GetIsDeletedAsync(5).Returns(false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.HardDeletePageAsync(5));

        await _repository.DidNotReceive().HardDeleteAsync(Arg.Any<int>());
    }

    // --- HardDeletePageAsync_ThrowsInvalidOperation_WhenPageNotFound ---

    [Fact]
    public async Task HardDeletePageAsync_ThrowsInvalidOperation_WhenPageNotFound()
    {
        _repository.GetByIdIncludingDeletedAsync(99).Returns((WikiPageDto?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.HardDeletePageAsync(99));

        await _repository.DidNotReceive().HardDeleteAsync(Arg.Any<int>());
    }

    // --- HardDeletePageAsync_DelegatesToRepo_WhenPageIsSoftDeleted ---

    [Fact]
    public async Task HardDeletePageAsync_DelegatesToRepo_WhenPageIsSoftDeleted()
    {
        _repository.GetByIdIncludingDeletedAsync(5)
            .Returns(new WikiPageDto { Id = 5, Title = "Deleted page", Slug = "deleted-page" });
        _repository.GetIsDeletedAsync(5).Returns(true);

        await _sut.HardDeletePageAsync(5);

        await _repository.Received(1).HardDeleteAsync(5);
    }

    // --- HardDeletePageAsync_RunsInsideTransaction ---

    [Fact]
    public async Task HardDeletePageAsync_RunsInsideTransaction()
    {
        _repository.GetByIdIncludingDeletedAsync(5)
            .Returns(new WikiPageDto { Id = 5, Title = "Deleted page", Slug = "deleted-page" });
        _repository.GetIsDeletedAsync(5).Returns(true);

        await _sut.HardDeletePageAsync(5);

        await _repository.Received(1).ExecuteInTransactionAsync(Arg.Any<Func<Task>>());
    }
}
