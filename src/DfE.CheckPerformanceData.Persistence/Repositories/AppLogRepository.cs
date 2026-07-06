using System.Runtime.CompilerServices;
using DfE.CheckPerformanceData.Application.Logging;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class AppLogRepository(IPortalDbContext context) : IAppLogRepository
{
    public async Task<int> BulkInsertAsync(IReadOnlyList<AppLogDto> logs, CancellationToken cancellationToken)
    {
        if (logs.Count == 0) return 0;

        // Add is faster than AddRange for small batches (~50 rows) and avoids a spurious LINQ
        // materialisation of the input list.
        foreach (var dto in logs)
        {
            context.AppLogs.Add(new AppLog
            {
                Timestamp = dto.Timestamp,
                Level = dto.Level,
                Category = dto.Category,
                Message = dto.Message,
                Exception = dto.Exception,
                EventId = dto.EventId,
                StateJson = dto.StateJson,
                RequestPath = dto.RequestPath,
                UserId = dto.UserId,
                CorrelationId = dto.CorrelationId
            });
        }
        return await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AppLogPage> SearchAsync(AppLogQuery query, CancellationToken cancellationToken)
    {
        var q = BuildFilteredQuery(query);

        var total = await q.CountAsync(cancellationToken);
        var rows = await q
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(a => new AppLogDto
            {
                Id = a.Id,
                Timestamp = a.Timestamp,
                Level = a.Level,
                Category = a.Category,
                Message = a.Message,
                Exception = a.Exception,
                EventId = a.EventId,
                StateJson = a.StateJson,
                RequestPath = a.RequestPath,
                UserId = a.UserId,
                CorrelationId = a.CorrelationId
            })
            .ToListAsync(cancellationToken);

        // Distinct-levels + distinct-categories drive the filter dropdowns on the admin page.
        // Cached on the paged response so we don't hit the DB again per request.
        var levels = await context.AppLogs
            .Select(a => a.Level).Distinct().OrderBy(l => l).ToListAsync(cancellationToken);
        var categories = await context.AppLogs
            .Select(a => a.Category).Distinct().OrderBy(c => c).ToListAsync(cancellationToken);

        return new AppLogPage(rows, total, categories, levels);
    }

    public async IAsyncEnumerable<AppLogDto> StreamAsync(
        AppLogQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var q = BuildFilteredQuery(query).OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id);

        await foreach (var a in q.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return new AppLogDto
            {
                Id = a.Id,
                Timestamp = a.Timestamp,
                Level = a.Level,
                Category = a.Category,
                Message = a.Message,
                Exception = a.Exception,
                EventId = a.EventId,
                StateJson = a.StateJson,
                RequestPath = a.RequestPath,
                UserId = a.UserId,
                CorrelationId = a.CorrelationId
            };
        }
    }

    public Task TruncateAsync(CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlRawAsync(
            @"TRUNCATE ""AppLogs"" RESTART IDENTITY;", cancellationToken);

    private IQueryable<AppLog> BuildFilteredQuery(AppLogQuery query)
    {
        var q = context.AppLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Level))
            q = q.Where(a => a.Level == query.Level);

        if (!string.IsNullOrWhiteSpace(query.Category))
            q = q.Where(a => a.Category == query.Category);

        if (query.FromUtc is DateTime from)
            q = q.Where(a => a.Timestamp >= from);

        if (query.ToUtc is DateTime to)
            q = q.Where(a => a.Timestamp <= to);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ILIKE = case-insensitive contains on Postgres; EF.Functions.ILike is untranslated
            // to raw SQL. Match Message OR Exception because operators sometimes only remember
            // the exception type, sometimes the message copy.
            var pattern = $"%{query.Search}%";
            q = q.Where(a =>
                EF.Functions.ILike(a.Message, pattern)
                || (a.Exception != null && EF.Functions.ILike(a.Exception!, pattern)));
        }

        return q;
    }
}
