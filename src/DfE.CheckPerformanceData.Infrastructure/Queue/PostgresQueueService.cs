using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Infrastructure.Queue;

public sealed class PostgresQueueService : IQueueService
{
    private readonly IPortalDbContext _dbContext;

    public PostgresQueueService(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task EnqueueAsync<T>(string queueName, T message, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<QueueMessage?> DequeueAsync(string queueName, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task AckAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task DeadLetterAsync(Guid messageId, string reason, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
