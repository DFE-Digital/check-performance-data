using DfE.CheckPerformanceData.Application.Common;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentBlocks;

public class ContentBlockServiceTests
{
    private readonly IContentBlockRepository _repository = Substitute.For<IContentBlockRepository>();
    private readonly IHtmlRenderingService _htmlRenderer = Substitute.For<IHtmlRenderingService>();
    private readonly ContentBlockService _sut;

    public ContentBlockServiceTests()
    {
        _sut = new ContentBlockService(_repository, _htmlRenderer);

        _repository.ExecuteInTransactionAsync(Arg.Any<Func<Task>>())
            .Returns(ci => ((Func<Task>)ci[0])());
    }

    // --- GetByKeyAsync ---

    [Fact]
    public async Task GetByKeyAsync_WhenBlockExists_ReturnsEnrichedDto()
    {
        var block = MakeBlock(blockType: "Content", value: "# Hello");
        _repository.GetByKeyAsync("home-banner").Returns(block);
        _htmlRenderer.RenderHtml("# Hello").Returns("<h1>Hello</h1>");

        var result = await _sut.GetByKeyAsync("home-banner");

        Assert.NotNull(result);
        Assert.Equal(block.Id, result.Id);
        Assert.Equal("<h1>Hello</h1>", result.ValueHtml);
    }

    [Fact]
    public async Task GetByKeyAsync_WhenBlockNotFound_ReturnsNull()
    {
        _repository.GetByKeyAsync("missing").ReturnsNull();

        var result = await _sut.GetByKeyAsync("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByKeyAsync_WhenBlockTypeIsNotContent_ValueHtmlIsNull()
    {
        var block = MakeBlock(blockType: "Script", value: "alert(1)");
        _repository.GetByKeyAsync("script-block").Returns(block);

        var result = await _sut.GetByKeyAsync("script-block");

        Assert.NotNull(result);
        Assert.Null(result.ValueHtml);
        _htmlRenderer.DidNotReceive().RenderHtml(Arg.Any<string?>());
    }

    // --- SaveAsync ---

    [Fact]
    public async Task SaveAsync_WhenKeyDoesNotExist_CreatesBlockAndVersion()
    {
        var dto = new SaveContentBlockDto { Key = "new-key", BlockType = "Content", Value = "New value" };
        var created = MakeBlock(id: 1, key: "new-key", blockType: "Content", value: "New value");

        _repository.GetByKeyAsync("new-key").ReturnsNull();
        _repository.AddBlockAsync("new-key", "Content", "New value").Returns(created);
        _htmlRenderer.RenderHtml("New value").Returns("<p>New value</p>");

        var result = await _sut.SaveAsync(dto);

        Assert.Equal(1, result.Id);
        Assert.Equal("<p>New value</p>", result.ValueHtml);
        await _repository.Received(1).AddBlockAsync("new-key", "Content", "New value");
        await _repository.Received(1).AddVersionAsync(1, "New value", 1);
    }

    [Fact]
    public async Task SaveAsync_WhenKeyDoesNotExist_AndOriginalValueDiffers_WritesBaselineThenEdit()
    {
        var dto = new SaveContentBlockDto
        {
            Key = "new-key",
            BlockType = "Content",
            Value = "Edited value",
            OriginalValue = "Default value"
        };
        var created = MakeBlock(id: 1, key: "new-key", blockType: "Content", value: "Edited value");

        _repository.GetByKeyAsync("new-key").ReturnsNull();
        _repository.AddBlockAsync("new-key", "Content", "Edited value").Returns(created);

        await _sut.SaveAsync(dto);

        await _repository.Received(1).AddBlockAsync("new-key", "Content", "Edited value");
        Received.InOrder(() =>
        {
            _repository.AddVersionAsync(1, "Default value", 1);
            _repository.AddVersionAsync(1, "Edited value", 2);
        });
    }

    [Fact]
    public async Task SaveAsync_WhenKeyDoesNotExist_AndOriginalValueEqualsValue_WritesSingleVersion()
    {
        var dto = new SaveContentBlockDto
        {
            Key = "new-key",
            BlockType = "Content",
            Value = "Same value",
            OriginalValue = "Same value"
        };
        var created = MakeBlock(id: 1, key: "new-key", blockType: "Content", value: "Same value");

        _repository.GetByKeyAsync("new-key").ReturnsNull();
        _repository.AddBlockAsync("new-key", "Content", "Same value").Returns(created);

        await _sut.SaveAsync(dto);

        await _repository.Received(1).AddVersionAsync(1, "Same value", 1);
        await _repository.DidNotReceive().AddVersionAsync(Arg.Any<int>(), Arg.Any<string>(), 2);
    }

    [Fact]
    public async Task SaveAsync_WhenKeyDoesNotExist_AndOriginalValueIsNullOrEmpty_WritesSingleVersion()
    {
        var dto = new SaveContentBlockDto
        {
            Key = "new-key",
            BlockType = "Content",
            Value = "Edited value",
            OriginalValue = null
        };
        var created = MakeBlock(id: 1, key: "new-key", blockType: "Content", value: "Edited value");

        _repository.GetByKeyAsync("new-key").ReturnsNull();
        _repository.AddBlockAsync("new-key", "Content", "Edited value").Returns(created);

        await _sut.SaveAsync(dto);

        await _repository.Received(1).AddVersionAsync(1, "Edited value", 1);
        await _repository.DidNotReceive().AddVersionAsync(Arg.Any<int>(), Arg.Any<string>(), 2);
    }

    [Fact]
    public async Task SaveAsync_WhenValueUnchanged_ReturnsExistingWithoutUpdate()
    {
        var existing = MakeBlock(id: 5, key: "k", blockType: "Content", value: "Same");
        var dto = new SaveContentBlockDto { Key = "k", BlockType = "Content", Value = "Same" };

        _repository.GetByKeyAsync("k").Returns(existing);
        _htmlRenderer.RenderHtml("Same").Returns("<p>Same</p>");

        var result = await _sut.SaveAsync(dto);

        Assert.Equal(5, result.Id);
        await _repository.DidNotReceive().UpdateValueAsync(Arg.Any<int>(), Arg.Any<string>());
        await _repository.DidNotReceive().AddVersionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>());
    }

    [Fact]
    public async Task SaveAsync_WhenValueChanged_UpdatesAndCreatesNewVersion()
    {
        var existing = MakeBlock(id: 3, key: "k", blockType: "Content", value: "Old");
        var updated = MakeBlock(id: 3, key: "k", blockType: "Content", value: "New");
        var dto = new SaveContentBlockDto { Key = "k", BlockType = "Content", Value = "New" };

        _repository.GetByKeyAsync("k").Returns(existing, updated);
        _repository.GetMaxVersionNumberAsync(3).Returns(2);
        _htmlRenderer.RenderHtml("New").Returns("<p>New</p>");

        var result = await _sut.SaveAsync(dto);

        Assert.Equal("<p>New</p>", result.ValueHtml);
        await _repository.Received(1).UpdateValueAsync(3, "New");
        await _repository.Received(1).AddVersionAsync(3, "New", 3);
    }

    [Fact]
    public async Task SaveAsync_WhenValueChanged_AndNoExistingVersions_BaselinesCurrentValueAsV1()
    {
        var existing = MakeBlock(id: 3, key: "k", blockType: "Content", value: "Original");
        var updated = MakeBlock(id: 3, key: "k", blockType: "Content", value: "Edited");
        var dto = new SaveContentBlockDto { Key = "k", BlockType = "Content", Value = "Edited" };

        _repository.GetByKeyAsync("k").Returns(existing, updated);
        _repository.GetMaxVersionNumberAsync(3).Returns(0);

        await _sut.SaveAsync(dto);

        await _repository.Received(1).UpdateValueAsync(3, "Edited");
        Received.InOrder(() =>
        {
            _repository.AddVersionAsync(3, "Original", 1);
            _repository.AddVersionAsync(3, "Edited", 2);
        });
    }

    [Fact]
    public async Task SaveAsync_WhenBlockNotFoundAfterUpdate_Throws()
    {
        var existing = MakeBlock(id: 3, key: "k", blockType: "Content", value: "Old");
        var dto = new SaveContentBlockDto { Key = "k", BlockType = "Content", Value = "New" };

        _repository.GetByKeyAsync("k").Returns(existing, (ContentBlockDto?)null);
        _repository.GetMaxVersionNumberAsync(3).Returns(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SaveAsync(dto));
    }

    // --- GetVersionsAsync ---

    [Fact]
    public async Task GetVersionsAsync_ReturnsVersionsFromRepository()
    {
        var versions = new List<ContentBlockVersionDto>
        {
            new() { Id = 1, VersionNumber = 1, Value = "v1" },
            new() { Id = 2, VersionNumber = 2, Value = "v2" }
        };
        _repository.GetVersionsByKeyAsync("k").Returns(versions);

        var result = await _sut.GetVersionsAsync("k");

        Assert.Equal(2, result.Count);
        Assert.Equal(versions, result);
    }

    // --- RevertToVersionAsync ---

    [Fact]
    public async Task RevertToVersionAsync_RevertsAndCreatesNewVersion()
    {
        var block = MakeBlock(id: 10, key: "k", blockType: "Content", value: "Current");
        var version = new ContentBlockVersionDto { Id = 5, VersionNumber = 1, Value = "Old value" };
        var reverted = MakeBlock(id: 10, key: "k", blockType: "Content", value: "Old value");

        _repository.GetByKeyAsync("k").Returns(block, reverted);
        _repository.GetVersionByIdAsync(5).Returns(version);
        _repository.GetMaxVersionNumberAsync(10).Returns(3);
        _htmlRenderer.RenderHtml("Old value").Returns("<p>Old value</p>");

        var result = await _sut.RevertToVersionAsync("k", 5);

        Assert.Equal("<p>Old value</p>", result.ValueHtml);
        await _repository.Received(1).UpdateValueAsync(10, "Old value");
        await _repository.Received(1).AddVersionAsync(10, "Old value", 4);
    }

    [Fact]
    public async Task RevertToVersionAsync_WhenBlockNotFound_Throws()
    {
        _repository.GetByKeyAsync("missing").ReturnsNull();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RevertToVersionAsync("missing", 1));
    }

    [Fact]
    public async Task RevertToVersionAsync_WhenVersionNotFound_Throws()
    {
        var block = MakeBlock(id: 1, key: "k");
        _repository.GetByKeyAsync("k").Returns(block);
        _repository.GetVersionByIdAsync(999).ReturnsNull();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RevertToVersionAsync("k", 999));
    }

    // --- Helpers ---

    private static ContentBlockDto MakeBlock(
        int id = 1,
        string key = "test-key",
        string blockType = "Content",
        string value = "Test value") => new()
    {
        Id = id,
        Key = key,
        BlockType = blockType,
        Value = value
    };
}
