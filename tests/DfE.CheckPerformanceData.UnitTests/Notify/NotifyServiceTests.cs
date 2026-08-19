using DfE.CheckPerformanceData.Application.Notify;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;

namespace DfE.CheckPerformanceData.UnitTests.Notify;

public sealed class NotifyServiceTests
{
    private readonly INotifyEmailClient _client = Substitute.For<INotifyEmailClient>();
    private readonly NotifySettings _settings = new()
    {
        ApiKey = "fake-api-key",
        SubmissionNotificationTemplateId = "sub-template-id",
        PupilDataCheckConfirmTemplateId = "confirm-template-id",
        PupilDataCheckWithdrawTemplateId = "withdraw-template-id",
        WithdrawNotificationTemplateId = "withdraw-notif-template-id",
        DlqThresholdTemplateId = "dlq-template-id",
        BulkSubmissionNotificationTemplateId = "bulk-template-id"
    };
    private const string DeadlineText = "28 February 2025";
    private static readonly EmailSubstitutions Substitutions =
        new("KS4 June", "Pupil", "updated in the Autumn");
    private readonly ILogger<Infrastructure.Notify.NotifyService> _logger =
        Substitute.For<ILogger<Infrastructure.Notify.NotifyService>>();
    private readonly Infrastructure.Notify.NotifyService _sut;

    public NotifyServiceTests()
    {
        _sut = new Infrastructure.Notify.NotifyService(
            _client,
            Options.Create(_settings),
            _logger);
    }

    [Fact]
    public async Task SendNotificationsAsync_SendsToAllRecipients()
    {
        var recipients = new[] { "alice@school.edu", "bob@school.edu" };

        await _sut.SendNotificationsAsync(
            "REF001",
            DeadlineText,
            recipients,
            NotificationType.SubmissionConfirmed,
            Substitutions);

        await _client.Received(1).SendEmailAsync(
            recipients[0],
            Arg.Any<string>(),
            Arg.Any<Dictionary<string, object>>());

        await _client.Received(1).SendEmailAsync(
            recipients[1],
            Arg.Any<string>(),
            Arg.Any<Dictionary<string, object>>());
    }

    [Fact]
    public async Task SendNotificationsAsync_UsesCorrectTemplateForSubmissionConfirmed()
    {
        var recipients = new[] { "test@school.edu" };

        await _sut.SendNotificationsAsync(
            "REF001",
            DeadlineText,
            recipients,
            NotificationType.SubmissionConfirmed,
            Substitutions);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            _settings.SubmissionNotificationTemplateId,
            Arg.Any<Dictionary<string, object>>());
    }

    [Fact]
    public async Task SendNotificationsAsync_UsesCorrectTemplateForDataCheckConfirmed()
    {
        var recipients = new[] { "test@school.edu" };

        await _sut.SendNotificationsAsync(
            "REF001",
            DeadlineText,
            recipients,
            NotificationType.DataCheckConfirmed,
            Substitutions);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            _settings.PupilDataCheckConfirmTemplateId,
            Arg.Any<Dictionary<string, object>>());
    }

    [Fact]
    public async Task SendNotificationsAsync_IncludesUrlInPersonalisation_WhenProvided()
    {
        var recipients = new[] { "test@school.edu" };
        var url = "https://example.com/submit-others";

        await _sut.SendNotificationsAsync(
            "REF001",
            DeadlineText,
            recipients,
            NotificationType.SubmissionConfirmed,
            Substitutions,
            url);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p =>
                p.ContainsKey("submit others url") && p["submit others url"].ToString() == url));
    }

    [Fact]
    public async Task SendNotificationsAsync_BulkSubmission_UsesBulkTemplateAndListsReferences()
    {
        var settings = new NotifySettings
        {
            ApiKey = "x",
            BulkSubmissionNotificationTemplateId = "bulk-template-id"
        };
        var client = Substitute.For<INotifyEmailClient>();
        var sut = new Infrastructure.Notify.NotifyService(client, Options.Create(settings), _logger);

        await sut.SendNotificationsAsync(
            referenceNumber: "",
            deadline: "5pm on Friday 26 June 2026",
            recipientEmails: new[] { "a@x.gov.uk" },
            notificationType: NotificationType.BulkSubmissionConfirmed,
            substitutions: Substitutions,
            url: "https://link",
            referenceNumbers: new[] { "REF001", "REF002" });

        await client.Received(1).SendEmailAsync(
            "a@x.gov.uk",
            "bulk-template-id",
            Arg.Is<Dictionary<string, object>>(p =>
                p.ContainsKey("references") &&
                ((string)p["references"]).Contains("REF001") &&
                ((string)p["references"]).Contains("REF002")));
    }

    [Fact]
    public async Task SendDlqThresholdEmailAsync_SendsToSpecifiedEmail()
    {
        await _sut.SendDlqThresholdEmailAsync("ops@school.edu", 10, 5);

        await _client.Received(1).SendEmailAsync(
            "ops@school.edu",
            _settings.DlqThresholdTemplateId,
            Arg.Any<Dictionary<string, object>>());
    }

    [Theory]
    [InlineData(NotificationType.SubmissionConfirmed)]
    [InlineData(NotificationType.BulkSubmissionConfirmed)]
    [InlineData(NotificationType.DataCheckConfirmed)]
    [InlineData(NotificationType.DataCheckWithdrawn)]
    [InlineData(NotificationType.AmendmentWithdrawn)]
    public async Task SendNotificationsAsync_IncludesCeNameKey_ForAllNotificationTypes(NotificationType type)
    {
        var recipients = new[] { "test@school.edu" };

        await _sut.SendNotificationsAsync("REF001", DeadlineText, recipients, type, Substitutions);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p =>
                p.ContainsKey("ce name") && p["ce name"].ToString() == "KS4 June"));
    }

    [Theory]
    [InlineData(NotificationType.DataCheckConfirmed)]
    [InlineData(NotificationType.DataCheckWithdrawn)]
    [InlineData(NotificationType.AmendmentWithdrawn)]
    public async Task SendNotificationsAsync_IncludesLearnerNounKey_ForDataCheckAndAmendmentWithdrawnTypes(NotificationType type)
    {
        var recipients = new[] { "test@school.edu" };

        await _sut.SendNotificationsAsync("REF001", DeadlineText, recipients, type, Substitutions);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p =>
                p.ContainsKey("learner noun") && p["learner noun"].ToString() == "Pupil"));
    }

    [Theory]
    [InlineData(NotificationType.SubmissionConfirmed)]
    [InlineData(NotificationType.BulkSubmissionConfirmed)]
    public async Task SendNotificationsAsync_DoesNotIncludeLearnerNounKey_ForSubmissionTypes(NotificationType type)
    {
        var recipients = new[] { "test@school.edu" };

        await _sut.SendNotificationsAsync("REF001", DeadlineText, recipients, type, Substitutions);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p => !p.ContainsKey("learner noun")));
    }

    [Theory]
    [InlineData(NotificationType.SubmissionConfirmed)]
    [InlineData(NotificationType.BulkSubmissionConfirmed)]
    [InlineData(NotificationType.DataCheckConfirmed)]
    [InlineData(NotificationType.DataCheckWithdrawn)]
    [InlineData(NotificationType.AmendmentWithdrawn)]
    public async Task SendNotificationsAsync_IncludesTurnaroundCommitmentKey_WhenNonEmpty(NotificationType type)
    {
        var recipients = new[] { "test@school.edu" };

        await _sut.SendNotificationsAsync("REF001", DeadlineText, recipients, type, Substitutions);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p =>
                p.ContainsKey("turnaround commitment") && p["turnaround commitment"].ToString() == "updated in the Autumn"));
    }

    [Theory]
    [InlineData(NotificationType.SubmissionConfirmed)]
    [InlineData(NotificationType.BulkSubmissionConfirmed)]
    [InlineData(NotificationType.DataCheckConfirmed)]
    [InlineData(NotificationType.DataCheckWithdrawn)]
    [InlineData(NotificationType.AmendmentWithdrawn)]
    public async Task SendNotificationsAsync_OmitsTurnaroundCommitmentKey_WhenEmpty(NotificationType type)
    {
        var recipients = new[] { "test@school.edu" };
        var emptySubstitutions = new EmailSubstitutions("KS4 June", "Pupil", "");

        await _sut.SendNotificationsAsync("REF001", DeadlineText, recipients, type, emptySubstitutions);

        await _client.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<Dictionary<string, object>>(p => !p.ContainsKey("turnaround commitment")));
    }

    [Fact]
    public async Task SendNotificationsAsync_CatchesException_DoesNotRethrow()
    {
        var recipients = new[] { "fail@school.edu" };
        _client.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, object>>())
            .Returns(x => throw new InvalidOperationException("Notify API failure"));

        var exception = await Record.ExceptionAsync(() =>
            _sut.SendNotificationsAsync("REF001", DeadlineText, recipients, NotificationType.SubmissionConfirmed, Substitutions));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendNotificationsAsync_LogsErrorOnFailure()
    {
        var recipients = new[] { "fail@school.edu" };
        _client.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, object>>())
            .Returns(x => throw new InvalidOperationException("Notify API failure"));

        await _sut.SendNotificationsAsync("REF001", DeadlineText, recipients, NotificationType.SubmissionConfirmed, Substitutions);

        _logger.Received(1).Log(
            Arg.Is<LogLevel>(l => l == LogLevel.Error),
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("REF001") && o.ToString()!.Contains("fail@school.edu")),
            Arg.Is<Exception>(e => e.Message.Contains("Notify API failure")),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task SendNotificationsAsync_ContinuesSendingAfterFailure()
    {
        var recipients = new[] { "fail@school.edu", "success@school.edu" };
        _client.SendEmailAsync(recipients[0], Arg.Any<string>(), Arg.Any<Dictionary<string, object>>())
            .Returns(x => throw new InvalidOperationException("First email fails"));
        _client.SendEmailAsync(recipients[1], Arg.Any<string>(), Arg.Any<Dictionary<string, object>>())
            .Returns(Task.CompletedTask);

        await _sut.SendNotificationsAsync("REF001", DeadlineText, recipients, NotificationType.SubmissionConfirmed, Substitutions);

        await _client.Received(1).SendEmailAsync(recipients[1], Arg.Any<string>(), Arg.Any<Dictionary<string, object>>());
    }

    [Fact]
    public async Task SendDlqThresholdEmailAsync_CatchesException_DoesNotRethrow()
    {
        _client.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, object>>())
            .Returns(x => throw new InvalidOperationException("Notify API failure"));

        var exception = await Record.ExceptionAsync(() =>
            _sut.SendDlqThresholdEmailAsync("ops@school.edu", 10, 5));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendNotificationsAsync_RetriesOnTransientHttpException_WithPipeline()
    {
        var pipeline = CreateRetryPipeline(maxRetries: 2);
        var sut = CreateSutWithPipeline(pipeline);
        var recipients = new[] { "retry@school.edu" };
        var callCount = 0;
        _client.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, object>>())
            .Returns(x =>
            {
                callCount++;
                if (callCount <= 2)
                    throw new HttpRequestException("Transient network error");
                return Task.CompletedTask;
            });

        await sut.SendNotificationsAsync("REF001", DeadlineText, recipients, NotificationType.SubmissionConfirmed, Substitutions);

        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task SendNotificationsAsync_DoesNotRetryOnTransientFailure_WithoutPipeline()
    {
        var recipients = new[] { "noretry@school.edu" };
        var callCount = 0;
        _client.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, object>>())
            .Returns(x =>
            {
                callCount++;
                throw new HttpRequestException("Transient network error");
            });

        await _sut.SendNotificationsAsync("REF001", DeadlineText, recipients, NotificationType.SubmissionConfirmed, Substitutions);

        Assert.Equal(1, callCount);
    }

    private static Polly.ResiliencePipeline CreateRetryPipeline(int maxRetries)
    {
        var builder = new Polly.ResiliencePipelineBuilder();
        builder.AddRetry(new Polly.Retry.RetryStrategyOptions
        {
            MaxRetryAttempts = maxRetries,
            DelayGenerator = _ => ValueTask.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(1)),
            ShouldHandle = new Polly.PredicateBuilder().Handle<HttpRequestException>(),
            OnRetry = _ => ValueTask.CompletedTask
        });
        return builder.Build();
    }

    private Infrastructure.Notify.NotifyService CreateSutWithPipeline(Polly.ResiliencePipeline pipeline)
    {
        return new Infrastructure.Notify.NotifyService(
            _client,
            Options.Create(_settings),
            _logger,
            pipeline);
    }
}
