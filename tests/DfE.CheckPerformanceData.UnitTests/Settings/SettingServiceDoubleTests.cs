using DfE.CheckPerformanceData.Application.Settings;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Settings;

// Locks ISettingService.GetDoubleAsync — required by the seed-sample-search-data page
// for the persisted per-event seconds rate (SearchAnalytics:SeedSecondsPerEvent, default
// 0.1). Same semantics as GetIntAsync: parse the stored value; fall back to the
// code-declared default on missing / malformed values; use InvariantCulture so a
// "0.10" stored on one machine reads the same on a comma-decimal locale.
public sealed class SettingServiceDoubleTests
{
    private readonly ISettingRepository _repository = Substitute.For<ISettingRepository>();
    private readonly SettingService _sut;

    public SettingServiceDoubleTests()
    {
        _sut = new SettingService(_repository);
        _repository.GetAllAsync().Returns(new Dictionary<string, string>());
    }

    [Fact]
    public async Task GetDoubleAsync_ParsesStoredValue_InvariantCulture()
    {
        _repository.GetValueAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent)
            .Returns("0.075");

        var value = await _sut.GetDoubleAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent);

        Assert.Equal(0.075, value, precision: 6);
    }

    [Fact]
    public async Task GetDoubleAsync_FallsBackToDefault_WhenStoredValueMissing()
    {
        _repository.GetValueAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent)
            .Returns((string?)null);

        var value = await _sut.GetDoubleAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent);

        // Default declared in SettingDefinitions is "0.1".
        Assert.Equal(0.1, value, precision: 6);
    }

    [Fact]
    public async Task GetDoubleAsync_FallsBackToDefault_WhenStoredValueUnparseable()
    {
        _repository.GetValueAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent)
            .Returns("not-a-number");

        var value = await _sut.GetDoubleAsync(SettingKeys.SearchAnalyticsSeedSecondsPerEvent);

        Assert.Equal(0.1, value, precision: 6);
    }
}
