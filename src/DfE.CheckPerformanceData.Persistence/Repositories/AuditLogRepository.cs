using System.Runtime.CompilerServices;
using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

/// <summary>
/// The audit log's read side (AB#294592). Filters, counts, orders and pages in SQL. Two rules:
/// (1) no JSON and no Guid.ToString() in SQL — the window filter materialises the window's run ids
/// in C# and compares EntityId against a string[] (= ANY); (2) NewValues is projected only for
/// EgressRun rows, so no other entity's payload is ever read out of the table.
/// </summary>
public sealed class AuditLogRepository(IPortalDbContext db) : IAuditLogRepository
{
    private sealed class Raw
    {
        public long Id { get; init; }
        public DateTime Timestamp { get; init; }
        public string? UserId { get; init; }
        public string EntityType { get; init; } = string.Empty;
        public string EntityId { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string? EgressPayload { get; init; }
    }

    public async Task<AuditLogPage> ListAsync(AuditLogFilter filter, int page, int pageSize, CancellationToken ct)
    {
        if (pageSize < 1) pageSize = 1;
        var query = await FilteredAsync(filter, ct);

        var total = await query.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var raw = await Project(Ordered(query).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);
        var titles = await TitlesAsync(ct);
        return new AuditLogPage(raw.Select(r => Decorate(r, titles)).ToList(), total, page, pageSize);
    }

    public async IAsyncEnumerable<AuditLogRow> StreamAsync(AuditLogFilter filter, [EnumeratorCancellation] CancellationToken ct)
    {
        // Everything else runs before the reader opens: Npgsql allows one open command per connection.
        var titles = await TitlesAsync(ct);
        var query = await FilteredAsync(filter, ct);
        await foreach (var raw in Project(Ordered(query)).AsAsyncEnumerable().WithCancellation(ct))
            yield return Decorate(raw, titles);
    }

    public async Task<IReadOnlyList<string>> ListActivitiesAsync(CancellationToken ct)
    {
        var present = await db.AuditEntries.AsNoTracking().Select(a => a.EntityType).Distinct().ToListAsync(ct);
        // Always offered, so egress can be isolated (and shown empty) before the first transfer.
        if (!present.Contains(AuditActivities.Egress)) present.Add(AuditActivities.Egress);
        return present.OrderBy(AuditActivities.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<IQueryable<AuditEntry>> FilteredAsync(AuditLogFilter filter, CancellationToken ct)
    {
        var query = db.AuditEntries.AsNoTracking();
        if (filter.Activity is { } activity)
            query = query.Where(a => a.EntityType == activity);
        if (filter.Outcome is { } outcome)
        {
            var action = outcome == AuditOutcome.Success ? AuditActivities.TransferAction : AuditActivities.TransferFailedAction;
            query = query.Where(a => a.EntityType == AuditActivities.Egress && a.Action == action);
        }
        if (filter.WindowId is { } windowId)
        {
            // A window has a handful of runs; their ids as text are what the egress rows' EntityId holds.
            var runIds = (await db.EgressRuns.AsNoTracking().Where(r => r.WindowId == windowId).Select(r => r.Id).ToListAsync(ct))
                .Select(id => id.ToString()).ToArray();
            var windowText = windowId.ToString();
            query = query.Where(a =>
                (a.EntityType == AuditActivities.Egress && runIds.Contains(a.EntityId)) ||
                (a.EntityType == AuditActivities.CheckingWindow && a.EntityId == windowText));
        }
        return query;
    }

    private static IOrderedQueryable<AuditEntry> Ordered(IQueryable<AuditEntry> query) =>
        query.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id);

    private static IQueryable<Raw> Project(IQueryable<AuditEntry> query) => query.Select(a => new Raw
    {
        Id = a.Id,
        Timestamp = a.Timestamp,
        UserId = a.UserId,
        EntityType = a.EntityType,
        EntityId = a.EntityId,
        Action = a.Action,
        EgressPayload = a.EntityType == AuditActivities.Egress ? a.NewValues : null
    });

    private async Task<IReadOnlyDictionary<Guid, string>> TitlesAsync(CancellationToken ct) =>
        await db.CheckingWindows.AsNoTracking().Select(w => new { w.Id, w.Title }).ToDictionaryAsync(w => w.Id, w => w.Title, ct);

    private static AuditLogRow Decorate(Raw raw, IReadOnlyDictionary<Guid, string> titles)
    {
        Guid? windowId = null;
        string? userName = null;
        IReadOnlyList<string> outputTypes = [];

        if (raw.EntityType == AuditActivities.Egress)
        {
            var payload = EgressAuditPayload.TryParse(raw.EgressPayload);
            windowId = payload?.WindowId;
            userName = payload?.TransferredBy;
            outputTypes = payload?.OutputTypes ?? [];
        }
        else if (raw.EntityType == AuditActivities.CheckingWindow && Guid.TryParse(raw.EntityId, out var id))
        {
            windowId = id;
        }

        var title = windowId is { } w && titles.TryGetValue(w, out var t) ? t : null;
        return new AuditLogRow(raw.Id, DateTime.SpecifyKind(raw.Timestamp, DateTimeKind.Utc), raw.UserId, userName,
            raw.EntityType, raw.EntityId, raw.Action, windowId, title, outputTypes, AuditActivities.OutcomeOf(raw.EntityType, raw.Action));
    }
}
