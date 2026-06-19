using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.RulesEngineWorker.Zendesk;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

// The development-only fake captures each "created" ticket into the shared dev outbox table
// instead of calling real Zendesk, and returns a synthetic response so the consumer's
// CrmId / idempotency path still completes. These tests pin that capture contract.
public sealed class DevOutboxZendeskServiceTests
{
    private readonly IPortalDbContext _dbContext = Substitute.For<IPortalDbContext>();
    private readonly DbSet<DevZendeskTicket> _outbox = Substitute.For<DbSet<DevZendeskTicket>, IQueryable<DevZendeskTicket>>();

    public DevOutboxZendeskServiceTests()
    {
        _dbContext.DevZendeskTickets.Returns(_outbox);
    }

    private static CreateTicketRequestDto SampleRequest() => new()
    {
        Ticket = new CreateTicketDto
        {
            Subject = "CPMD Auto-Approved: INC-ACC (REF-ABC123)",
            Description = "Request REF-ABC123 details",
            Status = "open",
            Priority = "normal",
            Type = "task",
        },
    };

    [Fact]
    public async Task CreateTicketAsync_WritesAnOutboxRow()
    {
        DevZendeskTicket? captured = null;
        _outbox.When(x => x.Add(Arg.Any<DevZendeskTicket>()))
            .Do(ci => captured = ci.Arg<DevZendeskTicket>());

        var sut = new DevOutboxZendeskService(_dbContext);

        await sut.CreateTicketAsync(SampleRequest());

        Assert.NotNull(captured);
        Assert.Equal("REF-ABC123", captured!.ReferenceNumber);
        Assert.Equal("CPMD Auto-Approved: INC-ACC (REF-ABC123)", captured.Subject);
        Assert.Equal("normal", captured.Priority);
        Assert.Equal("open", captured.Status);
        Assert.False(string.IsNullOrWhiteSpace(captured.RawJson));
        Assert.Contains("INC-ACC", captured.RawJson);
        await _dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateTicketAsync_ReturnsUsableSyntheticTicketId()
    {
        DevZendeskTicket? captured = null;
        _outbox.When(x => x.Add(Arg.Any<DevZendeskTicket>()))
            .Do(ci => captured = ci.Arg<DevZendeskTicket>());

        var sut = new DevOutboxZendeskService(_dbContext);

        var response = await sut.CreateTicketAsync(SampleRequest());

        Assert.NotNull(response);
        Assert.True(response.Ticket.Id > 0);
        Assert.NotNull(captured);
        Assert.Equal(captured!.TicketId, response.Ticket.Id);
    }
}
