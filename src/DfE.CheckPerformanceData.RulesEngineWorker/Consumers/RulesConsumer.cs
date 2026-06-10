using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.RulesEngineWorker.Consumers;

public sealed class RulesConsumer : BackgroundService
{
    private readonly IQueueService _queueService;
    private readonly IRulesProvider _rulesProvider;
    private readonly IRulesEngine _rulesEngine;
    private readonly IRuleContextMapper _contextMapper;
    private readonly IPortalDbContext _dbContext;

    public RulesConsumer(
        IQueueService queueService,
        IRulesProvider rulesProvider,
        IRulesEngine rulesEngine,
        IRuleContextMapper contextMapper,
        IPortalDbContext dbContext)
    {
        _queueService = queueService;
        _rulesProvider = rulesProvider;
        _rulesEngine = rulesEngine;
        _contextMapper = contextMapper;
        _dbContext = dbContext;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        throw new NotImplementedException();

    internal Task ProcessMessageBodyAsync(string messageBody, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
