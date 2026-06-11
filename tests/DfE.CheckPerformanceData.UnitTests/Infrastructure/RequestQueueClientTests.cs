using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Infrastructure.QueueStorage;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure;

public sealed class RequestQueueClientTests
{
    private readonly QueueClient _queueClient = Substitute.For<QueueClient>();
    private readonly RequestQueueClient _sut;

    public RequestQueueClientTests()
    {
        _sut = new RequestQueueClient(_queueClient);
    }

    [Fact]
    public async Task EnqueueRequestAsync_CreatesQueueBeforeSending()
    {
        // Same pattern as the blob clients: CreateIfNotExists before every write,
        // so a fresh storage account doesn't 404 the first submission.
        var callOrder = new List<string>();
        _queueClient.CreateIfNotExistsAsync()
            .Returns(_ => { callOrder.Add("create"); return Task.FromResult<Response>(null!); });
        _queueClient.SendMessageAsync(Arg.Any<string>())
            .Returns(_ => { callOrder.Add("send"); return Task.FromResult(Substitute.For<Response<SendReceipt>>()); });

        await _sut.EnqueueRequestAsync(Document());

        Assert.Equal(["create", "send"], callOrder);
    }

    [Fact]
    public async Task EnqueueRequestAsync_SendsBody_TheWorkerParserCanRead()
    {
        string? body = null;
        _queueClient.SendMessageAsync(Arg.Do<string>(b => body = b))
            .Returns(Task.FromResult(Substitute.For<Response<SendReceipt>>()));

        await _sut.EnqueueRequestAsync(Document());

        Assert.NotNull(body);
        var parsed = RequestDocumentParser.Parse(body!);
        Assert.NotNull(parsed);
        Assert.Equal("Remove - pupil-died", parsed!.RequestTypeCode);
        Assert.Equal(402, parsed.Pupil.Pincl);
    }

    private static RequestDocument Document() => new()
    {
        ReferenceNumber = "REF-1",
        SubmittedAt = DateTime.UtcNow,
        SubmittedBy = new UserDetails { UserId = "u", DisplayName = "A" },
        CheckingWindowId = Guid.NewGuid(),
        CheckingWindowType = "KS4June",
        RequestTypeCode = "Remove - pupil-died",
        School = new SchoolDetails { Urn = "1", Name = "S" },
        Pupil = new PupilDetails
        {
            Id = "p", CypmdId = "c", Firstname = "B", Surname = "S",
            DateOfBirth = "01/01/2010", Sex = "M", Age = 15, Upn = "U", Pincl = 402
        },
        Answers = []
    };
}
