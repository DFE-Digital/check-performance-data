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
        _repository.GetValueAsync(SettingKeys.CmsPageLength).Returns((string?)null);

        var value = await _sut.GetValueAsync(SettingKeys.CmsPageLength);

        Assert.Equal("20", value);
    }

    [Fact]
    public async Task GetValueAsync_ReturnsStoredValue_WhenPresent()
    {
        _repository.GetValueAsync(SettingKeys.CmsPageLength).Returns("35");

        var value = await _sut.GetValueAsync(SettingKeys.CmsPageLength);

        Assert.Equal("35", value);
    }

    [Fact]
    public async Task GetIntAsync_ParsesStoredValue()
    {
        _repository.GetValueAsync(SettingKeys.CmsPageLength).Returns("35");

        Assert.Equal(35, await _sut.GetIntAsync(SettingKeys.CmsPageLength));
    }

    [Fact]
    public async Task GetIntAsync_FallsBackToDefault_WhenStoredValueNotNumeric()
    {
        _repository.GetValueAsync(SettingKeys.CmsPageLength).Returns("not-a-number");

        Assert.Equal(20, await _sut.GetIntAsync(SettingKeys.CmsPageLength));
    }

    [Fact]
    public async Task GetBoolAsync_ReturnsTrue_WhenStoredValueTrue()
    {
        _repository.GetValueAsync(SettingKeys.DlqFullPayloadEnabled).Returns("true");

        Assert.True(await _sut.GetBoolAsync(SettingKeys.DlqFullPayloadEnabled));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("tRuE")]
    public async Task GetBoolAsync_IsCaseInsensitive_ForTrue(string stored)
    {
        _repository.GetValueAsync(SettingKeys.DlqFullPayloadEnabled).Returns(stored);

        Assert.True(await _sut.GetBoolAsync(SettingKeys.DlqFullPayloadEnabled));
    }

    [Fact]
    public async Task GetBoolAsync_ReturnsFalse_WhenStoredValueFalse()
    {
        _repository.GetValueAsync(SettingKeys.DlqFullPayloadEnabled).Returns("false");

        Assert.False(await _sut.GetBoolAsync(SettingKeys.DlqFullPayloadEnabled));
    }

    [Fact]
    public async Task GetBoolAsync_FallsBackToDefault_WhenStoredValueNull()
    {
        _repository.GetValueAsync(SettingKeys.DlqFullPayloadEnabled).Returns((string?)null);

        // Default for DlqFullPayloadEnabled is "false".
        Assert.False(await _sut.GetBoolAsync(SettingKeys.DlqFullPayloadEnabled));
    }

    [Fact]
    public async Task GetBoolAsync_FallsBackToDefault_WhenStoredValueBlank()
    {
        _repository.GetValueAsync(SettingKeys.DlqFullPayloadEnabled).Returns("   ");

        Assert.False(await _sut.GetBoolAsync(SettingKeys.DlqFullPayloadEnabled));
    }

    [Fact]
    public async Task GetBoolAsync_FallsBackToDefault_WhenStoredValueUnparseable()
    {
        _repository.GetValueAsync(SettingKeys.DlqFullPayloadEnabled).Returns("banana");

        Assert.False(await _sut.GetBoolAsync(SettingKeys.DlqFullPayloadEnabled));
    }

    [Fact]
    public async Task GetBoolAsync_UsesTrueDefault_WhenDefinitionDefaultIsTrue()
    {
        // NotifyUseFake defaults to "true".
        _repository.GetValueAsync(SettingKeys.NotifyUseFake).Returns((string?)null);

        Assert.True(await _sut.GetBoolAsync(SettingKeys.NotifyUseFake));
    }

    [Fact]
    public async Task GetBoolAsync_Throws_ForUnknownKey()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.GetBoolAsync("Bogus:Key"));
    }

    [Fact]
    public async Task SaveAsync_UpsertsTrimmedValue_WhenProvided()
    {
        await _sut.SaveAsync(SettingKeys.CmsPageLength, "  50 ");

        await _repository.Received(1).UpsertAsync(SettingKeys.CmsPageLength, "50");
        await _repository.DidNotReceive().DeleteAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task SaveAsync_DeletesSetting_WhenValueBlank_RevertingToDefault()
    {
        await _sut.SaveAsync(SettingKeys.CmsPageLength, "   ");

        await _repository.Received(1).DeleteAsync(SettingKeys.CmsPageLength);
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

        var pageLength = Assert.Single(items, i => i.Key == SettingKeys.CmsPageLength);
        Assert.Equal("20", pageLength.Value);
        Assert.True(pageLength.IsDefault);
    }

    [Fact]
    public async Task GetAllWithValuesAsync_MarksStoredSettings_AsNotDefault()
    {
        _repository.GetAllAsync().Returns(
            new Dictionary<string, string> { [SettingKeys.CmsPageLength] = "40" });

        var items = await _sut.GetAllWithValuesAsync();

        var pageLength = Assert.Single(items, i => i.Key == SettingKeys.CmsPageLength);
        Assert.Equal("40", pageLength.Value);
        Assert.False(pageLength.IsDefault);
    }

    [Fact]
    public async Task GetAllWithValuesAsync_CarriesSettingKind_ToViewItem()
    {
        var items = await _sut.GetAllWithValuesAsync();

        var boolItem = Assert.Single(items, i => i.Key == SettingKeys.DlqFullPayloadEnabled);
        Assert.Equal(SettingKind.Bool, boolItem.Kind);

        var intItem = Assert.Single(items, i => i.Key == SettingKeys.CmsPageLength);
        Assert.Equal(SettingKind.Int, intItem.Kind);
    }
}
