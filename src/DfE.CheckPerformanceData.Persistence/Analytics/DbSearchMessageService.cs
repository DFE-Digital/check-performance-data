using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Analytics;

// Postgres implementation of the search-messages service. Owns messages-only writes and
// deletes — the events sink deliberately does not expose a messages-purge method
// (two owners for one table would drift out of sync). Implements the retention purge,
// the user-facing create path, and the admin inbox surface (unread-count badge, paged
// list, detail, mark-read, and session-scoped purge for the support-lookup flow).
//
// PurgeExpiredMessagesAsync uses a CTE-batched DELETE loop for the same reason
// DbSearchAnalyticsSink does: at 10k+ rows a single-shot ExecuteDeleteAsync takes an
// ACCESS EXCLUSIVE lock long enough to stall the messages-inbox unread-count badge and
// the admin listing. The 10k batch bound keeps each lock window well under one second.
public sealed class DbSearchMessageService : ISearchMessageService
{
    // Purge batch size — matches the events sink's cadence for symmetry.
    private const int PurgeBatchSize = 10_000;

    // First-line preview length — matches the D-12 inbox spec (~80 chars). Truncation
    // happens server-side in the projection so Razor never sees the full message body,
    // and no view template has to know the length.
    private const int PreviewLength = 80;

    private readonly IPortalDbContext _dbContext;

    public DbSearchMessageService(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<long> CreateAsync(
        string sessionId,
        string whatLookingFor,
        string? whatGot,
        string? email,
        CancellationToken cancellationToken)
    {
        // Server-owned SubmittedAtUtc — the caller never supplies this. IsRead starts
        // false so the inbox unread-count badge picks it up; ReadByAdminSub and ReadAtUtc
        // are populated by the admin mark-read flow, never on insert.
        var entity = new SearchMessage
        {
            SessionId = sessionId,
            SubmittedAtUtc = DateTime.UtcNow,
            WhatLookingFor = whatLookingFor,
            WhatGot = whatGot,
            Email = email,
            IsRead = false,
        };

        _dbContext.SearchMessages.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken)
    {
        // Backed by ix_search_messages_is_read so the badge is safe on every admin page
        // load regardless of table size. No AsNoTracking on a scalar read — Count does
        // not materialise entities.
        return await _dbContext.SearchMessages
            .Where(m => !m.IsRead)
            .CountAsync(cancellationToken);
    }

    public async Task<PagedResult<SearchMessageSummary>> GetInboxAsync(
        SearchMessageSortField sortField,
        bool sortDesc,
        string? filterText,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;

        var query = _dbContext.SearchMessages.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterText))
        {
            // Case-insensitive substring on the message body only (WhatLookingFor). ILIKE
            // is Postgres-native and Npgsql maps EF.Functions.ILike to it directly; the
            // server-side match avoids pulling every row into memory to filter.
            var like = "%" + filterText.Trim() + "%";
            query = query.Where(m => EF.Functions.ILike(m.WhatLookingFor, like));
        }

        var total = await query.CountAsync(cancellationToken);

        // Sort — flip Asc/Desc on the caller-selected field. The stable secondary sort on
        // Id guarantees deterministic pagination when two rows share the same primary key
        // value (SubmittedAtUtc collisions during a bulk-second-boundary load).
        query = (sortField, sortDesc) switch
        {
            (SearchMessageSortField.IsRead, true)     => query.OrderByDescending(m => m.IsRead).ThenByDescending(m => m.Id),
            (SearchMessageSortField.IsRead, false)    => query.OrderBy(m => m.IsRead).ThenByDescending(m => m.Id),
            (SearchMessageSortField.SubmittedAt, false) => query.OrderBy(m => m.SubmittedAtUtc).ThenBy(m => m.Id),
            _                                          => query.OrderByDescending(m => m.SubmittedAtUtc).ThenByDescending(m => m.Id),
        };

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                m.Id,
                m.SubmittedAtUtc,
                m.SessionId,
                HasEmail = m.Email != null,
                m.IsRead,
                m.WhatLookingFor,
            })
            .ToListAsync(cancellationToken);

        var summaries = rows
            .Select(r => new SearchMessageSummary(
                r.Id,
                DateTime.SpecifyKind(r.SubmittedAtUtc, DateTimeKind.Utc),
                r.SessionId,
                r.HasEmail,
                r.IsRead,
                BuildPreview(r.WhatLookingFor)))
            .ToList();

        return new PagedResult<SearchMessageSummary>(summaries, total);
    }

    public async Task<SearchMessageDetail?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SearchMessages
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new SearchMessageDetail(
                m.Id,
                m.SubmittedAtUtc,
                m.SessionId,
                m.WhatLookingFor,
                m.WhatGot,
                m.Email,
                m.IsRead,
                m.ReadByAdminSub,
                m.ReadAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) return null;

        // Postgres round-trips timestamptz with Kind=Unspecified after Npgsql read; normalise
        // to Utc so callers never see an ambiguous DateTimeKind.
        return row with
        {
            SubmittedAtUtc = DateTime.SpecifyKind(row.SubmittedAtUtc, DateTimeKind.Utc),
            ReadAtUtc = row.ReadAtUtc is null
                ? null
                : DateTime.SpecifyKind(row.ReadAtUtc.Value, DateTimeKind.Utc),
        };
    }

    public async Task MarkReadAsync(
        long id,
        string readByAdminSub,
        DateTime readAtUtc,
        CancellationToken cancellationToken)
    {
        // The WHERE clause filters unread rows only, so the update is idempotent by
        // construction — a re-invoke against an already-read row is a zero-row update
        // and the first admin's attribution is preserved. ExecuteUpdateAsync issues a
        // single UPDATE with no entity fetch (no change tracker, no auto-audit — the
        // message row itself carries the audit fields ReadByAdminSub + ReadAtUtc).
        await _dbContext.SearchMessages
            .Where(m => m.Id == id && !m.IsRead)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(m => m.IsRead, true)
                    .SetProperty(m => m.ReadByAdminSub, readByAdminSub)
                    .SetProperty(m => m.ReadAtUtc, readAtUtc),
                cancellationToken);
    }

    public async Task<SessionPurgeCounts> PurgeSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        // Messages only — events + results are deleted separately by the controller so
        // they can share a transaction with the audit-entry write. ExecuteDeleteAsync is
        // one statement, no fetch; per-session counts are small (typically dozens) so
        // the lock window is tiny.
        var deleted = await _dbContext.SearchMessages
            .Where(m => m.SessionId == sessionId)
            .ExecuteDeleteAsync(cancellationToken);
        return new SessionPurgeCounts(deleted);
    }

    public async Task<IReadOnlyList<SearchMessageSummary>> GetForSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SearchMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderByDescending(m => m.SubmittedAtUtc)
            .Select(m => new
            {
                m.Id,
                m.SubmittedAtUtc,
                m.SessionId,
                HasEmail = m.Email != null,
                m.IsRead,
                m.WhatLookingFor,
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new SearchMessageSummary(
                r.Id,
                DateTime.SpecifyKind(r.SubmittedAtUtc, DateTimeKind.Utc),
                r.SessionId,
                r.HasEmail,
                r.IsRead,
                BuildPreview(r.WhatLookingFor)))
            .ToList();
    }

    public async Task<int> PurgeExpiredMessagesAsync(
        TimeSpan olderThan, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var totalDeleted = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var affected = await _dbContext.Database.ExecuteSqlRawAsync(
                @"WITH victims AS (
                      SELECT id FROM search_messages
                      WHERE submitted_at_utc < {0}
                      LIMIT " + PurgeBatchSize + @"
                  )
                  DELETE FROM search_messages WHERE id IN (SELECT id FROM victims);",
                new object[] { cutoff },
                cancellationToken);

            if (affected <= 0)
            {
                break;
            }

            totalDeleted += affected;

            if (affected < PurgeBatchSize)
            {
                break;
            }
        }

        return totalDeleted;
    }

    // Collapses newlines to single spaces then truncates to PreviewLength characters.
    // Used by every inbox-row projection so no view template has to know the length or
    // the newline-handling rule.
    private static string BuildPreview(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        var collapsed = body.Replace('\r', ' ').Replace('\n', ' ');
        return collapsed.Length <= PreviewLength
            ? collapsed
            : collapsed.Substring(0, PreviewLength);
    }
}
