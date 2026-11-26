using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace DfE.CheckPerformanceData.Web.Impersonation;

/// <summary>
/// Keeps impersonated journey state in process memory. Entries expire after 15 minutes
/// without access and are removed when the impersonation generation ends.
/// </summary>
public sealed class InMemoryImpersonationPreviewStore(IMemoryCache cache, TimeProvider timeProvider) : IImpersonationPreviewStore
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);
    private readonly object _cacheLock = new();

    public bool TryGet(string binding, string generation, string key, out byte[]? value)
    {
        var session = GetSession(binding, generation, create: false);
        if (session is not null && session.Values.TryGetValue(key, out var stored))
        {
            value = stored.ToArray();
            return true;
        }

        value = null;
        return false;
    }

    public void Set(string binding, string generation, string key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var session = GetSession(binding, generation, create: true)!;
        session.Values[key] = value.ToArray();
    }

    public void Remove(string binding, string generation, string key)
    {
        GetSession(binding, generation, create: false)?.Values.TryRemove(key, out _);
    }

    public IReadOnlyCollection<string> GetKeys(string binding, string generation)
    {
        var session = GetSession(binding, generation, create: false);
        return session is null ? [] : session.Values.Keys.ToArray();
    }

    public void RemoveSession(string binding, string generation)
    {
        lock (_cacheLock)
        {
            cache.Remove(CacheKey(binding, generation));
        }
    }

    public string SaveEvidence(string binding, string generation, Guid windowId, byte[] content)
    {
        var storedName = Guid.NewGuid().ToString("N");
        Set(binding, generation, EvidenceKey(windowId, storedName), content);
        return storedName;
    }

    public byte[]? GetEvidence(string binding, string generation, Guid windowId, string storedName) =>
        TryGet(binding, generation, EvidenceKey(windowId, storedName), out var content) ? content : null;

    public void RemoveEvidence(string binding, string generation, Guid windowId, string storedName) =>
        Remove(binding, generation, EvidenceKey(windowId, storedName));

    private PreviewSession? GetSession(string binding, string generation, bool create)
    {
        var key = CacheKey(binding, generation);
        lock (_cacheLock)
        {
            var now = timeProvider.GetUtcNow();
            if (cache.TryGetValue(key, out PreviewSession? existing) && existing is not null)
            {
                if (now - existing.LastActivityUtc >= IdleTimeout)
                {
                    cache.Remove(key);
                    existing = null;
                }
            }

            if (existing is not null)
            {
                existing.LastActivityUtc = now;
                cache.Set(key, existing, new MemoryCacheEntryOptions { SlidingExpiration = IdleTimeout });
                return existing;
            }

            if (!create) return null;

            var session = new PreviewSession(now);
            cache.Set(key, session, new MemoryCacheEntryOptions { SlidingExpiration = IdleTimeout });
            return session;
        }
    }

    private static string CacheKey(string binding, string generation) =>
        $"impersonation-preview:{binding}:{generation}";

    private static string EvidenceKey(Guid windowId, string storedName) =>
        $"evidence_{windowId:N}_{storedName}";

    private sealed class PreviewSession
    {
        public PreviewSession(DateTimeOffset lastActivityUtc) => LastActivityUtc = lastActivityUtc;

        public DateTimeOffset LastActivityUtc { get; set; }
        public ConcurrentDictionary<string, byte[]> Values { get; } = new(StringComparer.Ordinal);
    }
}
