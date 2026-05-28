using DfE.CheckPerformanceData.Application.Settings;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Settings;

public sealed class SettingServiceTests
{
    private readonly ISettingRepository _repository = Substitute.For<ISettingRepository>();
    private readonly SettingService _sut;

    public SettingServiceTests()
    {
        _sut = new SettingService(_repository);
        _repository.GetAllAsync().Returns(new Dictionary<string, string>());
    }

    [Fact]
    public async Task GetValueAsync_ReturnsCodeDefault_WhenNotStored()
    {
        _repository.GetValueAsync(SettingKeys.WikiPageLength).Returns((string?)null);

        var value = await _sut.GetValueAsync(SettingKeys.WikiPageLength);

        Assert.Equal("20", value);
    }

    [Fact]
    public async Task GetValueAsync_ReturnsStoredValue_WhenPresent()
    {
        _repository.GetValueAsync(SettingKeys.WikiPageLength).Returns("35");

        var value = await _sut.GetValueAsync(SettingKeys.WikiPageLength);

        Assert.Equal("35", value);
    }

    [Fact]
    public async Task GetIntAsync_ParsesStoredValue()
    {
        _repository.GetValueAsync(SettingKeys.WikiPageLength).Returns("35");

        Assert.Equal(35, await _sut.GetIntAsync(SettingKeys.WikiPageLength));
    }

    [Fact]
    public async Task GetIntAsync_FallsBackToDefault_WhenStoredValueNotNumeric()
    {
        _repository.GetValueAsync(SettingKeys.WikiPageLength).Returns("not-a-number");

        Assert.Equal(20, await _sut.GetIntAsync(SettingKeys.WikiPageLength));
    }

    [Fact]
    public async Task SaveAsync_UpsertsTrimmedValue_WhenProvided()
    {
        await _sut.SaveAsync(SettingKeys.WikiPageLength, "  50 ");

        await _repository.Received(1).UpsertAsync(SettingKeys.WikiPageLength, "50");
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task SaveAsync_DeletesSetting_WhenValueBlank_RevertingToDefault()
    {
        await _sut.SaveAsync(SettingKeys.WikiPageLength, "   ");

        await _repository.Received(1).DeleteAsync(SettingKeys.WikiPageLength);
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SaveAsync_Throws_ForUnknownKey()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SaveAsync("Bogus:Key", "x"));
    }

    [Fact]
    public async Task GetAllWithValuesAsync_ReturnsKnownSettings_WithDefaultFlag()
    {
        _repository.GetAllAsync().Returns(new Dictionary<string, string>());

        var items = await _sut.GetAllWithValuesAsync();

        var pageLength = Assert.Single(items, i => i.Key == SettingKeys.WikiPageLength);
        Assert.Equal("20", pageLength.Value);
        Assert.True(pageLength.IsDefault);
    }

    [Fact]
    public async Task GetAllWithValuesAsync_MarksStoredSettings_AsNotDefault()
    {
        _repository.GetAllAsync().Returns(
            new Dictionary<string, string> { [SettingKeys.WikiPageLength] = "40" });

        var items = await _sut.GetAllWithValuesAsync();

        var pageLength = Assert.Single(items, i => i.Key == SettingKeys.WikiPageLength);
        Assert.Equal("40", pageLength.Value);
        Assert.False(pageLength.IsDefault);
    }
}
