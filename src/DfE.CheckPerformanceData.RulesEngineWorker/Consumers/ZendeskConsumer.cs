using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Consumers;

public sealed class ZendeskConsumer : BackgroundService
{
    private readonly IQueueService _queueService;
    private readonly IZendeskService _zendeskService;
    private readonly IPortalDbContext _dbContext;

    public ZendeskConsumer(
        IQueueService queueService,
        IZendeskService zendeskService,
        IPortalDbContext dbContext)
    {
        _queueService = queueService;
        _zendeskService = zendeskService;
        _dbContext = dbContext;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        throw new NotImplementedException();

    internal Task ProcessMessageBodyAsync(string messageBody, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
