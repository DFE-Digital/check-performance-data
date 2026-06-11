using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DfE.CheckPerformanceData.Infrastructure.Queue;

public sealed class PostgresQueueService : IQueueService
{
    private const string PendingStatus = "pending";
    private const int MaxReasonLength = 1024;

    private readonly IPortalDbContext _dbContext;
    private readonly TimeSpan _visibilityTimeout;

    public PostgresQueueService(IPortalDbContext dbContext)
        : this(dbContext, new QueueOptions())
    {
    }

    public PostgresQueueService(IPortalDbContext dbContext, QueueOptions options)
    {
        _dbContext = dbContext;
        _visibilityTimeout = options.VisibilityTimeout;
    }

    public PostgresQueueService(IPortalDbContext dbContext, Microsoft.Extensions.Options.IOptions<QueueOptions> options)
        : this(dbContext, options.Value)
    {
    }

    public async Task EnqueueAsync<T>(string queueName, T message, CancellationToken cancellationToken = default)
    {
        var payload = message as string ?? JsonSerializer.Serialize(message);
        var now = DateTime.UtcNow;

        // Add via the caller's DbContext so the INSERT participates in any ambient
        // ExecuteInTransactionAsync — a rolled-back transaction drops the message too.
        var entity = new QueueMessageEntity
        {
            Id = Guid.NewGuid(),
            QueueName = queueName,
            Payload = payload,
            Attempts = 0,
            EnqueuedAtUtc = now,
            VisibleAfterUtc = now,
            Status = PendingStatus
        };

        _dbContext.QueueMessages.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Subsequent dequeue/delete operate on the row via raw SQL, so a tracked copy would
        // only go stale. Stop tracking it once it is durably written.
        Detach(entity);
    }

    public async Task<QueueMessage?> DequeueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        // Claim exactly one visible row. FOR UPDATE SKIP LOCKED guarantees two concurrent
        // consumers never claim the same row; pushing visible_after_utc forward gives the
        // claim a visibility timeout, so a crashed worker's row re-surfaces on its own.
        const string sql = @"
UPDATE queue_messages
SET visible_after_utc = @visibleAfter, attempts = attempts + 1
WHERE id = (
    SELECT id FROM queue_messages
    WHERE queue_name = @queueName AND status = @status AND visible_after_utc <= @now
    ORDER BY enqueued_at_utc
    FOR UPDATE SKIP LOCKED
    LIMIT 1
)
RETURNING id, queue_name, payload, attempts, enqueued_at_utc, visible_after_utc, status;";

        var now = DateTime.UtcNow;
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("queueName", queueName);
            command.Parameters.AddWithValue("status", PendingStatus);
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("visibleAfter", now + _visibilityTimeout);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new QueueMessage
            {
                Id = reader.GetGuid(0),
                QueueName = reader.GetString(1),
                Payload = reader.GetString(2),
                Attempts = reader.GetInt32(3),
                EnqueuedAt = reader.GetDateTime(4),
                VisibleAfterUtc = reader.GetDateTime(5),
                Status = QueueMessageStatus.InFlight
            };
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task AckAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.QueueMessages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (entity is null)
        {
            return;
        }

        _dbContext.QueueMessages.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeadLetterAsync(Guid messageId, string reason, CancellationToken cancellationToken = default)
    {
        // Read the row's current state straight from the database — a prior DequeueAsync claims
        // the row with raw SQL, so a tracked copy in this context could be stale on Attempts.
        var entity = await _dbContext.QueueMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (entity is null)
        {
            return;
        }

        var truncatedReason = reason.Length > MaxReasonLength
            ? reason[..MaxReasonLength]
            : reason;

        _dbContext.DeadLetters.Add(new DeadLetterEntity
        {
            Id = entity.Id,
            QueueName = entity.QueueName,
            Payload = entity.Payload,
            Reason = truncatedReason,
            PayloadHash = ComputeHash(entity.Payload),
            Attempts = entity.Attempts,
            EnqueuedAtUtc = entity.EnqueuedAtUtc,
            DeadLetteredAtUtc = DateTime.UtcNow
        });

        // Persist the dead-letter copy before removing the source row so a failure can never
        // drop the message without retaining it.
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _dbContext.QueueMessages
            .Where(m => m.Id == messageId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QueueDepth>> GetQueueDepthsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var grouped = await _dbContext.QueueMessages
            .GroupBy(m => m.QueueName)
            .Select(g => new
            {
                QueueName = g.Key,
                Depth = g.Count(),
                Oldest = g.Min(m => (DateTime?)m.EnqueuedAtUtc)
            })
            .ToListAsync(cancellationToken);

        return grouped
            .Select(g => new QueueDepth(
                g.QueueName,
                g.Depth,
                g.Oldest is null ? null : now - g.Oldest.Value))
            .ToList();
    }

    public async Task<IReadOnlyList<QueueMessageSummary>> GetTopMessagesAsync(string queueName, int count, CancellationToken cancellationToken = default)
    {
        // Oldest-first so the top-N shows the messages that have waited longest — the ones an
        // operator most wants to see. A sane upper bound keeps a hostile count value from
        // pulling the whole queue into memory.
        var take = Math.Clamp(count, 0, 100);
        if (take == 0)
        {
            return Array.Empty<QueueMessageSummary>();
        }

        var rows = await _dbContext.QueueMessages
            .Where(m => m.QueueName == queueName)
            .OrderBy(m => m.EnqueuedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows.Select(MapSummary).ToList();
    }

    public async Task<IReadOnlyList<QueueMessageSummary>> GetQueueMessagesAsync(string queueName, CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.QueueMessages
            .Where(m => m.QueueName == queueName)
            .OrderBy(m => m.EnqueuedAtUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(MapSummary).ToList();
    }

    public async Task<QueueMessageDetail?> GetMessageDetailAsync(string queueName, Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.QueueMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.QueueName == queueName, cancellationToken);

        return row is null
            ? null
            : new QueueMessageDetail(row.Id, row.QueueName, row.EnqueuedAtUtc, row.Attempts, row.Payload);
    }

    public async Task<IReadOnlyList<DlqMessage>> GetDlqMessagesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.DeadLetters
            .OrderByDescending(d => d.DeadLetteredAtUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<DlqMessage?> GetDlqMessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.DeadLetters
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        return row is null ? null : Map(row);
    }

    public async Task RedriveAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        if (messageIds.Count == 0)
        {
            return;
        }

        var idSet = messageIds.ToHashSet();
        var deadLetters = await _dbContext.DeadLetters
            .Where(d => idSet.Contains(d.Id))
            .ToListAsync(cancellationToken);

        if (deadLetters.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var requeued = new List<QueueMessageEntity>();
        foreach (var dead in deadLetters)
        {
            // Re-enqueue with the dead letter's original id so replaying the same redrive
            // twice cannot place a second copy on the source queue.
            var alreadyQueued = await _dbContext.QueueMessages
                .AnyAsync(m => m.Id == dead.Id, cancellationToken);

            if (!alreadyQueued)
            {
                var entity = new QueueMessageEntity
                {
                    Id = dead.Id,
                    QueueName = dead.QueueName,
                    Payload = dead.Payload,
                    Attempts = 0,
                    EnqueuedAtUtc = now,
                    VisibleAfterUtc = now,
                    Status = PendingStatus
                };
                _dbContext.QueueMessages.Add(entity);
                requeued.Add(entity);
            }

            _dbContext.DeadLetters.Remove(dead);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var entity in requeued)
        {
            Detach(entity);
        }
    }

    public async Task PurgeAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        if (messageIds.Count == 0)
        {
            return;
        }

        var idSet = messageIds.ToHashSet();
        var deadLetters = await _dbContext.DeadLetters
            .Where(d => idSet.Contains(d.Id))
            .ToListAsync(cancellationToken);

        if (deadLetters.Count == 0)
        {
            return;
        }

        _dbContext.DeadLetters.RemoveRange(deadLetters);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(TimeSpan retention, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - retention;
        var expired = await _dbContext.DeadLetters
            .Where(d => d.DeadLetteredAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return 0;
        }

        _dbContext.DeadLetters.RemoveRange(expired);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private void Detach(QueueMessageEntity entity)
    {
        if (_dbContext is DbContext context)
        {
            context.Entry(entity).State = EntityState.Detached;
        }
    }

    private static QueueMessageSummary MapSummary(QueueMessageEntity m) => new(
        m.Id,
        m.QueueName,
        m.EnqueuedAtUtc,
        m.Attempts);

    private static DlqMessage Map(DeadLetterEntity d) => new(
        d.Id,
        d.QueueName,
        d.Attempts,
        d.Reason,
        d.Payload,
        d.DeadLetteredAtUtc);

    private static string ComputeHash(string payload)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
