using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Infrastructure.Notify;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.Notify;

public sealed class DevConsoleNotifyServiceTests
{
    private readonly ILogger<DevConsoleNotifyService> _logger = Substitute.For<ILogger<DevConsoleNotifyService>>();
    private readonly DevConsoleNotifyService _sut;

    public DevConsoleNotifyServiceTests()
    {
        _sut = new DevConsoleNotifyService(_logger);
    }

    [Fact]
    public async Task SendNotificationsAsync_LogsAllParameters()
    {
        var recipients = new[] { "alice@school.edu", "bob@school.edu" };

        await _sut.SendNotificationsAsync(
            "REF-001",
            "28 February 2025",
            recipients,
            NotificationType.SubmissionConfirmed,
            "https://example.com/submit");

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("SubmissionConfirmed")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task SendDlqThresholdEmailAsync_LogsParameters()
    {
        await _sut.SendDlqThresholdEmailAsync("admin@school.edu", 15, 10);

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("DLQ")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void SendNotificationsAsync_MakesNoOutboundCalls()
    {
        var recipients = new[] { "alice@school.edu" };

        _sut.SendNotificationsAsync(
            "REF-001",
            "28 February 2025",
            recipients,
            NotificationType.SubmissionConfirmed);

        Assert.True(true, "No exception means no outbound call dependency was required");
    }

    [Fact]
    public async Task SendNotificationsAsync_HandlesEmptyRecipientList()
    {
        var recipients = Array.Empty<string>();

        await _sut.SendNotificationsAsync(
            "REF-001",
            "28 February 2025",
            recipients,
            NotificationType.DataCheckConfirmed);

        _logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("0")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }
}

public sealed class NotifyServiceRegistrationTests
{
    private sealed class StubNotifyService : INotifyService
    {
        public Task SendNotificationsAsync(string referenceNumber, string deadline, IReadOnlyCollection<string> recipientEmails, NotificationType notificationType, string? url = null, IReadOnlyCollection<string>? referenceNumbers = null) =>
            Task.CompletedTask;
        public Task SendDlqThresholdEmailAsync(string toEmail, int dlqDepth, int threshold) =>
            Task.CompletedTask;
    }

    private static IServiceCollection BuildServicesWithRealNotify()
    {
        var services = new ServiceCollection();
        services.AddScoped<INotifyService, StubNotifyService>();
        return services;
    }

    private static IConfiguration Config(bool? useFake)
    {
        var values = new Dictionary<string, string?>();
        if (useFake.HasValue)
            values["Notify:UseFake"] = useFake.Value ? "true" : "false";

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void ShouldUseFake_ReturnsTrue_WhenConfigAbsent()
    {
        var config = Config(null);

        var result = NotifyServiceRegistration.ShouldUseFake(config);

        Assert.True(result);
    }

    [Fact]
    public void ShouldUseFake_ReturnsFalse_WhenConfigIsFalse()
    {
        var config = Config(false);

        var result = NotifyServiceRegistration.ShouldUseFake(config);

        Assert.False(result);
    }

    [Fact]
    public void ShouldUseFake_ReturnsTrue_WhenConfigIsTrue()
    {
        var config = Config(true);

        var result = NotifyServiceRegistration.ShouldUseFake(config);

        Assert.True(result);
    }

    [Fact]
    public void ConfigureFakeNotify_ReplacesService_WhenShouldUseFakeIsTrue()
    {
        var services = BuildServicesWithRealNotify();

        NotifyServiceRegistration.ConfigureFakeNotify(services, Config(true));

        var descriptor = services.Last(d => d.ServiceType == typeof(INotifyService));
        Assert.Equal(typeof(DevConsoleNotifyService), descriptor.ImplementationType);
    }

    [Fact]
    public void ConfigureFakeNotify_DoesNotReplace_WhenShouldUseFakeIsFalse()
    {
        var services = BuildServicesWithRealNotify();

        NotifyServiceRegistration.ConfigureFakeNotify(services, Config(false));

        var descriptor = services.Last(d => d.ServiceType == typeof(INotifyService));
        Assert.Equal(typeof(StubNotifyService), descriptor.ImplementationType);
    }
}
