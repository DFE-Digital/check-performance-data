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

    public async Task<Guid> CreateAsync(
        string bundleJson, string? createdBy, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var session = new ContentStagingSession
        {
            Id = Guid.NewGuid(),
            BundleJson = bundleJson,
            CreatedBy = createdBy is { Length: > MaxCreatedByLength }
                ? createdBy[..MaxCreatedByLength]
                : createdBy,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(ContentStagingSessionDefaults.Lifetime)
        };

        dbContext.ContentStagingSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session.Id;
    }

    public async Task<string?> GetBundleJsonAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        // Expiry is applied in the query, not left to the sweep. A row that has aged out reads
        // as absent from the moment it expires, whether or not anything has deleted it yet.
        var now = _clock.GetUtcNow().UtcDateTime;
        return await dbContext.ContentStagingSessions
            .Where(s => s.Id == sessionId && s.ExpiresAtUtc > now)
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
