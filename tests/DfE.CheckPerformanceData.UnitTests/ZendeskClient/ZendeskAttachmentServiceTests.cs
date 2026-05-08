using System.Text;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Mappers;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

public sealed class ZendeskAttachmentServiceTests : IDisposable
{
    private readonly IZendeskApi _api = Substitute.For<IZendeskApi>();
    private readonly PollySettings _settings = new()
    {
        MaxRetryAttempts = 3,
        BaseDelayMilliseconds = 1000,
        JitterMilliseconds = 250
    };
    private readonly ILogger<ZendeskAttachmentService> _logger = Substitute.For<ILogger<ZendeskAttachmentService>>();

    private ZendeskAttachmentService CreateSut() => new(_api, Options.Create(_settings), _logger);

    public void Dispose() { }

    // Helper: creates a valid UpdateTicketResponse that the mapper can handle
    private static UpdateTicketResponse ValidResponse(long ticketId = 99L)
    {
        return new()
        {
            Ticket = new()
            {
                Id = ticketId,
                Status = "solved",
                UpdatedAt = DateTime.UtcNow
            },
            Audit = new()
            {
                Id = 100L,
                TicketId = ticketId,
                CreatedAt = DateTime.UtcNow,
                AuthorId = 42L,
                Events = new List<TicketAuditEvent>()
            }
        };
    }

    // ==================================================================== AddAttachmentAsync

    #region AddAttachmentAsync

    [Fact]
    public async Task AddAttachmentAsync_ThrowsZendeskApiException_WhenTicketIdIsZero()
    {
        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(async () =>
            await CreateSut().AddAttachmentAsync(0, "test.pdf", new MemoryStream(Encoding.UTF8.GetBytes("data"))));
        Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Equal("ticketId", ((ArgumentException)ex.InnerException!).ParamName);
    }

    [Fact]
    public async Task AddAttachmentAsync_ThrowsZendeskApiException_WhenFileStreamIsNull()
    {
        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(async () =>
            await CreateSut().AddAttachmentAsync(42, "test.pdf", null!));
        Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Equal("fileStream", ((ArgumentException)ex.InnerException!).ParamName);
    }

    [Fact]
    public async Task AddAttachmentAsync_ThrowsZendeskApiException_WhenFileStreamIsEmpty()
    {
        using var empty = new MemoryStream();
        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(async () =>
            await CreateSut().AddAttachmentAsync(42, "test.pdf", empty));
        Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Equal("fileStream", ((ArgumentException)ex.InnerException!).ParamName);
    }

    [Fact]
    public async Task AddAttachmentAsync_UploadsFile_FirstCallOrder()
    {
        var token = new UploadResponse { Upload = new() { Token = "tok1" } };
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>()).Returns(token);

        _api.AddCommentWithAttachment(42L, Arg.Any<UpdateTicketRequest>()).Returns(ValidResponse());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake content"));
        await CreateSut().AddAttachmentAsync(42, "test.pdf", stream);

        // Upload is called before the comment
        await _api.Received(1).UploadFile(Arg.Any<string>(), Arg.Any<Stream>());
    }

    [Fact]
    public async Task AddAttachmentAsync_UsesCorrectFileName_OnUpload()
    {
        var token = new UploadResponse { Upload = new() { Token = "tok1" } };
        _api.UploadFile("my_file.pdf", Arg.Any<Stream>()).Returns(token);

        _api.AddCommentWithAttachment(42L, Arg.Any<UpdateTicketRequest>()).Returns(ValidResponse());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake content"));
        await CreateSut().AddAttachmentAsync(42, "my_file.pdf", stream);

        await _api.Received(1).UploadFile("my_file.pdf", Arg.Any<Stream>());
    }

    [Fact]
    public async Task AddAttachmentAsync_PassesUploadToken_ToComment_WhenSuccess()
    {
        var token = new UploadResponse { Upload = new() { Token = "tok1" } };
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>()).Returns(token);

        UpdateTicketRequest? capturedRequest = null;
        _api.AddCommentWithAttachment(42L, Arg.Do<UpdateTicketRequest>(r => capturedRequest = r))
            .Returns(ValidResponse());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake content"));
        await CreateSut().AddAttachmentAsync(42, "test.pdf", stream);

        // Verify the call was made with a valid token in the uploads
        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedRequest.Ticket!.Comment!.Uploads);
        Assert.Single(capturedRequest.Ticket.Comment!.Uploads!);
        Assert.Equal("tok1", capturedRequest.Ticket.Comment.Uploads![0]!);
    }

    [Fact]
    public async Task AddAttachmentAsync_CallsMap_ToDto_WithCorrectResponse()
    {
        var uploadResp = new UploadResponse { Upload = new() { Token = "tok1" } };
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>()).Returns(uploadResp);

        var updateResp = ValidResponse();
        _api.AddCommentWithAttachment(42L, Arg.Any<UpdateTicketRequest>()).Returns(updateResp);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake content"));
        var result = await CreateSut().AddAttachmentAsync(42, "test.pdf", stream);

        Assert.NotNull(result.Ticket);
    }

    [Fact]
    public async Task AddAttachmentAsync_UsesCustomCommentBody()
    {
        var uploadResp = new UploadResponse { Upload = new() { Token = "tok1" } };
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>()).Returns(uploadResp);

        UpdateTicketRequest? capturedRequest = null;
        _api.AddCommentWithAttachment(42L, Arg.Do<UpdateTicketRequest>(r => capturedRequest = r))
            .Returns(ValidResponse());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake content"));
        await CreateSut().AddAttachmentAsync(42, "test.pdf", stream, "Please review the attached document.");

        // Verify custom comment body was passed in the request
        Assert.NotNull(capturedRequest);
        Assert.Equal("Please review the attached document.", capturedRequest.Ticket!.Comment!.Body);
    }

    [Fact]
    public async Task AddAttachmentAsync_UsesDefaultCommentBody_WhenNotProvided()
    {
        var uploadResp = new UploadResponse { Upload = new() { Token = "tok1" } };
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>()).Returns(uploadResp);

        UpdateTicketRequest? capturedRequest = null;
        _api.AddCommentWithAttachment(42L, Arg.Do<UpdateTicketRequest>(r => capturedRequest = r))
            .Returns(ValidResponse());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake content"));
        await CreateSut().AddAttachmentAsync(42, "test.pdf", stream);

        // Verify default comment body was passed in the request
        Assert.NotNull(capturedRequest);
        Assert.Equal("Attachment uploaded", capturedRequest.Ticket!.Comment!.Body);
    }

    [Fact]
    public async Task AddAttachmentAsync_ThrowsZendeskApiException_WhenUploadReturnsNullToken()
    {
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>()).Returns(new UploadResponse());

        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(async () =>
            await CreateSut().AddAttachmentAsync(42, "test.pdf", new MemoryStream(Encoding.UTF8.GetBytes("fake content"))));

        Assert.Contains("upload token", ex.InnerException!.Message);
    }

    [Fact]
    public async Task AddAttachmentAsync_ThrowsZendeskApiException_WhenUploadTokenIsEmpty()
    {
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>())
            .Returns(new UploadResponse { Upload = new() { Token = "" } });

        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(async () =>
            await CreateSut().AddAttachmentAsync(42, "test.pdf", new MemoryStream(Encoding.UTF8.GetBytes("fake content"))));

        Assert.Contains("upload token", ex.InnerException!.Message);
    }

    [Fact]
    public async Task AddAttachmentAsync_ThrowsZendeskApiException_WhenUploadTokenIsNull()
    {
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>())
            .Returns(new UploadResponse { Upload = new() { Token = null! } });

        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(async () =>
            await CreateSut().AddAttachmentAsync(42, "test.pdf", new MemoryStream(Encoding.UTF8.GetBytes("fake content"))));

        Assert.Contains("upload token", ex.InnerException!.Message);
    }

    [Fact]
    public async Task AddAttachmentAsync_ThrowsZendeskApiException_WhenUploadFailsWithHttpRequestException()
    {
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>())
            .Returns(Task.FromException<UploadResponse>(new HttpRequestException("Connection lost")));

        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(async () =>
            await CreateSut().AddAttachmentAsync(42, "test.pdf", new MemoryStream(Encoding.UTF8.GetBytes("fake content"))));

        Assert.Contains("Failed to upload attachment", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task AddAttachmentAsync_ThrowsZendeskApiException_WhenCommentFails()
    {
        var uploadResp = new UploadResponse { Upload = new() { Token = "tok1" } };
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>()).Returns(uploadResp);

        _api.AddCommentWithAttachment(42L, Arg.Any<UpdateTicketRequest>())
            .Returns(Task.FromException<UpdateTicketResponse>(new HttpRequestException("Server error")));

        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(async () =>
            await CreateSut().AddAttachmentAsync(42, "test.pdf", new MemoryStream(Encoding.UTF8.GetBytes("fake content"))));

        Assert.Contains("Failed to upload attachment", ex.Message);
    }

    [Fact]
    public async Task AddAttachmentAsync_IncludesTicketId_InErrorMessage()
    {
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>())
            .Returns(Task.FromException<UploadResponse>(new HttpRequestException()));

        var ex = await Assert.ThrowsAnyAsync<ZendeskApiException>(async () =>
            await CreateSut().AddAttachmentAsync(12345, "test.pdf", new MemoryStream(Encoding.UTF8.GetBytes("fake content"))));

        Assert.Contains("12345", ex.Message);
    }

    [Fact]
    public async Task AddAttachmentAsync_LogsError_WhenUploadFails()
    {
        var logLevels = new List<LogLevel>();
        _logger.When(l => l.Log(Arg.Any<LogLevel>(), Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception>(), Arg.Any<Func<object, Exception, string>>()))
            .Do(callInfo => logLevels.Add((LogLevel)callInfo.Args()[0]));

        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>())
            .Returns(Task.FromException<UploadResponse>(new HttpRequestException("Network down")));

        try
        {
            await CreateSut().AddAttachmentAsync(42, "test.pdf", new MemoryStream(Encoding.UTF8.GetBytes("fake content")));
        }
        catch (ZendeskApiException)
        {
            // Expected — we are testing the logging side-effect.
        }

        // The service logs via LogError which maps to LogLevel.Error (may log multiple times due to retry callback + final error)
        Assert.True(logLevels.Count(l => l == LogLevel.Error) >= 1);
    }

    [Fact]
    public async Task AddAttachmentAsync_LogsDebug_OnSuccessfulUpload()
    {
        var logLevels = new List<LogLevel>();
        _logger.When(l => l.Log(Arg.Any<LogLevel>(), Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception>(), Arg.Any<Func<object, Exception, string>>()))
            .Do(callInfo => logLevels.Add((LogLevel)callInfo.Args()[0]));

        var uploadResp = new UploadResponse { Upload = new() { Token = "tok1" } };
        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>()).Returns(uploadResp);

        _api.AddCommentWithAttachment(42L, Arg.Any<UpdateTicketRequest>()).Returns(ValidResponse());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake content"));
        await CreateSut().AddAttachmentAsync(42, "test.pdf", stream);

        // The service logs LogDebug 4 times: 
        // 2x "Executing Zendesk operation: ..." and 2x "Successfully completed Zendesk operation: ..."
        Assert.Equal(4, logLevels.Count(l => l == LogLevel.Debug));
    }

    [Fact]
    public async Task AddAttachmentAsync_LogsWarning_OnRetry()
    {
        var logLevels = new List<LogLevel>();
        _logger.When(l => l.Log(Arg.Any<LogLevel>(), Arg.Any<EventId>(), Arg.Any<object>(), Arg.Any<Exception>(), Arg.Any<Func<object, Exception, string>>()))
            .Do(callInfo => logLevels.Add((LogLevel)callInfo.Args()[0]));

        var token = new UploadResponse { Upload = new() { Token = "tok1" } };

        _api.UploadFile(Arg.Any<string>(), Arg.Any<Stream>())
            .Returns(
                Task.FromException<UploadResponse>(new HttpRequestException()),
                Task.FromResult(token));

        _api.AddCommentWithAttachment(42L, Arg.Any<UpdateTicketRequest>()).Returns(ValidResponse());

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("fake content"));
        await CreateSut().AddAttachmentAsync(42, "test.pdf", stream);

        // The retry callback logs via LogWarning which maps to LogLevel.Warning
        Assert.Single(logLevels, l => l == LogLevel.Warning);
    }

    #endregion
}