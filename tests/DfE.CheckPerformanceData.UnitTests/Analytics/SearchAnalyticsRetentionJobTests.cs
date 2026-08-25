using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.RulesEngineWorker.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Analytics;

// Unit-level contract for SearchAnalyticsRetentionJob's inner overload — the one that
// takes the collaborators directly, bypassing IServiceScopeFactory. Three facts pin the
// clamps that defend against a mis-configured retention setting:
//
//   1. Events cutoff is settings-driven and applied to ISearchAnalyticsSink; messages
//      cutoff is separately settings-driven and applied to ISearchMessageService (they
//      live on distinct interfaces so one owner per table set stays enforced).
//   2. An events setting above the 365-day hard-max clamps down; a malicious "retain
//      forever" (365000) cannot disable the retention ceiling.
//   3. A messages setting above the 730-day hard-max clamps down for the same reason;
//      the two clamps are independent because messages legitimately live longer than
//      events (support cases often reference weeks-old sessions).
public sealed class SearchAnalyticsRetentionJobTests
{
    private readonly ISearchAnalyticsSink _sink = Substitute.For<ISearchAnalyticsSink>();
    private readonly ISearchMessageService _messageService = Substitute.For<ISearchMessageService>();
    private readonly ISettingService _settings = Substitute.For<ISettingService>();

    private SearchAnalyticsRetentionJob CreateSut() => new(
        Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
        NullLogger<SearchAnalyticsRetentionJob>.Instance);

    [Fact]
    public async Task RunOnceAsync_DirectOverload_PurgesBothTablesUnderConfiguredRetention()
    {
        _settings.GetIntAsync(SettingKeys.SearchAnalyticsRetentionDays).Returns(45);
        _settings.GetIntAsync(SettingKeys.SearchAnalyticsMessageRetentionDays).Returns(365);
        _sink.PurgeExpiredAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);
        _messageService.PurgeExpiredMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);

        var sut = CreateSut();
        await sut.RunOnceAsync(_settings, _sink, _messageService, CancellationToken.None);

        await _sink.Received(1).PurgeExpiredAsync(
            TimeSpan.FromDays(45), Arg.Any<CancellationToken>());
        await _messageService.Received(1).PurgeExpiredMessagesAsync(
            TimeSpan.FromDays(365), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_EventsRetentionAboveHardMax_ClampsAt365Days()
    {
        _settings.GetIntAsync(SettingKeys.SearchAnalyticsRetentionDays).Returns(500);
        _settings.GetIntAsync(SettingKeys.SearchAnalyticsMessageRetentionDays).Returns(365);
        _sink.PurgeExpiredAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);
        _messageService.PurgeExpiredMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);

        var sut = CreateSut();
        await sut.RunOnceAsync(_settings, _sink, _messageService, CancellationToken.None);

        await _sink.Received(1).PurgeExpiredAsync(
            TimeSpan.FromDays(365), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnceAsync_MessagesRetentionAboveHardMax_ClampsAt730Days()
    {
        _settings.GetIntAsync(SettingKeys.SearchAnalyticsRetentionDays).Returns(90);
        _settings.GetIntAsync(SettingKeys.SearchAnalyticsMessageRetentionDays).Returns(800);
        _sink.PurgeExpiredAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);
        _messageService.PurgeExpiredMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);

        var sut = CreateSut();
        await sut.RunOnceAsync(_settings, _sink, _messageService, CancellationToken.None);

        await _messageService.Received(1).PurgeExpiredMessagesAsync(
            TimeSpan.FromDays(730), Arg.Any<CancellationToken>());
    }

    // Zero / negative settings clamp UP to 1 so a mistake in the admin editor can't
    // spin a hot loop deleting everything on every tick.
    [Fact]
    public async Task RunOnceAsync_EventsRetentionBelowMinimum_ClampsAtOneDay()
    {
        _settings.GetIntAsync(SettingKeys.SearchAnalyticsRetentionDays).Returns(0);
        _settings.GetIntAsync(SettingKeys.SearchAnalyticsMessageRetentionDays).Returns(365);
        _sink.PurgeExpiredAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);
        _messageService.PurgeExpiredMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);

        var sut = CreateSut();
        await sut.RunOnceAsync(_settings, _sink, _messageService, CancellationToken.None);

        await _sink.Received(1).PurgeExpiredAsync(
            TimeSpan.FromDays(1), Arg.Any<CancellationToken>());
    }
}
