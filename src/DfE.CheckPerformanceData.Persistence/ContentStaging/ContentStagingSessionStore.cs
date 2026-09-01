using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.ContentStaging;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.ContentStaging;

// Postgres-backed store for previewed-but-not-yet-imported bundles.
//
// TimeProvider rather than DateTime.UtcNow so the expiry boundary is testable without
// waiting a day for a row to age out.
public sealed class ContentStagingSessionStore(IPortalDbContext dbContext, TimeProvider? timeProvider = null)
    : IContentStagingSessionStore
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    // Matches the column width. CreatedBy is a breadcrumb for a support query, never a key or
    // an authorisation input, so clipping an improbably long address is strictly better than
    // failing the preview it was recorded on — and the column would otherwise throw 22001.
    private const int MaxCreatedByLength = 200;

    // Applied on both write and read, so a clipped address still matches its own session.
    private static string? Clip(string? value) =>
        value is { Length: > MaxCreatedByLength } ? value[..MaxCreatedByLength] : value;

    public async Task<Guid> CreateAsync(
        string bundleJson, string? createdBy, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        // One live session per operator. Nothing else bounds this table: the sweep only removes
        // rows already past their expiry and runs BEFORE the insert, so previewing repeatedly
        // stores a copy of the bundle each time and deletes none of them. At the upload ceiling
        // that is tens of megabytes per request, retained for a day, from a surface that needs
        // no confirm step to reach — enough to fill the database's storage, which on a managed
        // Postgres takes the whole service read-only rather than just the CMS.
        //
        // Dropping the operator's previous session matches the workflow anyway: an import is
        // reviewed and confirmed one at a time, and abandoning a preview to start another is
        // exactly the case where the first one is no longer wanted.
        var owner = Clip(createdBy);
        if (owner is not null)
        {
            await dbContext.ContentStagingSessions
                .Where(s => s.CreatedBy == owner)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var session = new ContentStagingSession
        {
            Id = Guid.NewGuid(),
            BundleJson = bundleJson,
            CreatedBy = Clip(createdBy),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(ContentStagingSessionDefaults.Lifetime)
        };

        dbContext.ContentStagingSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session.Id;
    }

    public async Task<string?> GetBundleJsonAsync(
        Guid sessionId, string? requestedBy, CancellationToken cancellationToken = default)
    {
        // Expiry is applied in the query, not left to the sweep. A row that has aged out reads
        // as absent from the moment it expires, whether or not anything has deleted it yet.
        // Ownership is applied the same way, so a session belonging to someone else is simply
        // not found rather than refused — the caller cannot tell the two apart.
        var now = _clock.GetUtcNow().UtcDateTime;
        var owner = Clip(requestedBy);
        return await dbContext.ContentStagingSessions
            .Where(s => s.Id == sessionId && s.ExpiresAtUtc > now)
            .Where(s => s.CreatedBy == owner || (s.CreatedBy == null && owner == null))
            .Select(s => s.BundleJson)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await dbContext.ContentStagingSessions
            .Where(s => s.Id == sessionId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return await dbContext.ContentStagingSessions
            .Where(s => s.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
