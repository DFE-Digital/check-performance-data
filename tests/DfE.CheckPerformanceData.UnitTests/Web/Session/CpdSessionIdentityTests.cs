using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Session;

// Direct cover for the app-owned analytics session identity. The middleware tests exercise
// the happy path end-to-end; these pin the edges that only show up off the request path —
// a background caller with no session at all, and a session the middleware has not loaded
// yet. Both must read as "no identity" rather than throwing or minting one, because the
// analytics sink treats null as "skip the write" and a mint here would attach a fresh id to
// a row that has no session behind it.
public sealed class CpdSessionIdentityTests
{
    [Fact]
    public void Peek_NullSession_ReturnsNull()
    {
        Assert.Null(CpdSessionIdentity.Peek(null));
    }

    [Fact]
    public void Peek_SessionNotYetLoaded_ReturnsNull_WithoutTriggeringALoad()
    {
        // IsAvailable is false until the session middleware has loaded. Reading through
        // anyway would force a load off whatever ambient scope happens to be current.
        var session = new FakeSession { IsAvailable = false };
        session.SetString(CpdSessionIdentity.IdKey, "would-be-visible-if-we-ignored-IsAvailable");

        Assert.Null(CpdSessionIdentity.Peek(session));
    }

    [Fact]
    public void Peek_LoadedSessionWithNoIdentityYet_ReturnsNull_AndDoesNotMintOne()
    {
        var session = new FakeSession();

        Assert.Null(CpdSessionIdentity.Peek(session));
        // Peek is a read. Minting is Ensure's job and belongs to the middleware.
        Assert.Empty(session.Keys);
    }

    [Fact]
    public void Peek_EmptyStoredValue_IsTreatedAsAbsent()
    {
        var session = new FakeSession();
        session.SetString(CpdSessionIdentity.IdKey, string.Empty);

        Assert.Null(CpdSessionIdentity.Peek(session));
    }

    [Fact]
    public void Ensure_MintsOnce_ThenReturnsTheSameValue()
    {
        var session = new FakeSession();

        var first = CpdSessionIdentity.Ensure(session);
        var second = CpdSessionIdentity.Ensure(session);

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.Equal(first, second);
        Assert.Equal(first, CpdSessionIdentity.Peek(session));
    }

    [Fact]
    public void Ensure_AfterTheSessionIsCleared_MintsAFreshId()
    {
        // This is the whole mechanism behind the absolute-lifetime cap: the identity lives
        // in the session, so the existing wipe rotates it without any cookie surgery.
        var session = new FakeSession();
        var before = CpdSessionIdentity.Ensure(session);

        session.Clear();
        var after = CpdSessionIdentity.Ensure(session);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Ensure_MintsDistinctIdsForDistinctSessions()
    {
        var a = CpdSessionIdentity.Ensure(new FakeSession());
        var b = CpdSessionIdentity.Ensure(new FakeSession());

        Assert.NotEqual(a, b);
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsAvailable { get; set; } = true;
        public string Id { get; } = "framework-session-id";
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value)
        {
            if (_store.TryGetValue(key, out var v)) { value = v; return true; }
            value = Array.Empty<byte>(); return false;
        }
    }
}
