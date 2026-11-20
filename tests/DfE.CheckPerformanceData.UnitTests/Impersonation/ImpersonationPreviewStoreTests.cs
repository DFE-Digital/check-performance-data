using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics.CodeAnalysis;

namespace DfE.CheckPerformanceData.Application.UnitTests.Impersonation;

public sealed class ImpersonationPreviewStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Values_AreIsolatedByBindingAndGeneration()
    {
        var store = CreateStore(new FakeTimeProvider(Start));
        store.Set("admin-a", "generation-a", "request_window", [1, 2, 3]);

        Assert.True(store.TryGet("admin-a", "generation-a", "request_window", out var value));
        Assert.Equal(new byte[] { 1, 2, 3 }, value);
        Assert.False(store.TryGet("admin-b", "generation-a", "request_window", out _));
        Assert.False(store.TryGet("admin-a", "generation-b", "request_window", out _));
    }

    [Fact]
    public void Values_ExpireAfterFifteenMinutesWithoutAccess()
    {
        var clock = new FakeTimeProvider(Start);
        var store = CreateStore(clock);
        store.Set("admin", "generation", "request_window", [1]);

        clock.Advance(TimeSpan.FromMinutes(15));

        Assert.False(store.TryGet("admin", "generation", "request_window", out _));
    }

    [Fact]
    public void Access_ExtendsTheIdleTimeout()
    {
        var clock = new FakeTimeProvider(Start);
        var store = CreateStore(clock);
        store.Set("admin", "generation", "request_window", [1]);

        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(store.TryGet("admin", "generation", "request_window", out _));
        clock.Advance(TimeSpan.FromMinutes(14));
        Assert.True(store.TryGet("admin", "generation", "request_window", out _));
        clock.Advance(TimeSpan.FromMinutes(15));

        Assert.False(store.TryGet("admin", "generation", "request_window", out _));
    }

    [Fact]
    public void RemoveSession_ClearsEveryPreviewKey()
    {
        var store = CreateStore(new FakeTimeProvider(Start));
        store.Set("admin", "generation", "request_window", [1]);
        store.Set("admin", "generation", "evidence_window_file", [2]);

        store.RemoveSession("admin", "generation");

        Assert.Empty(store.GetKeys("admin", "generation"));
        Assert.False(store.TryGet("admin", "generation", "request_window", out _));
    }

    [Fact]
    public void Evidence_IsAvailableOnlyInPreviewMemoryAndCanBeRemoved()
    {
        var store = CreateStore(new FakeTimeProvider(Start));
        var content = new byte[] { 9, 8, 7 };
        var windowId = Guid.NewGuid();

        var storedName = store.SaveEvidence("admin", "generation", windowId, content);

        Assert.True(Guid.TryParseExact(storedName, "N", out _));
        Assert.Equal(content, store.GetEvidence("admin", "generation", windowId, storedName));
        Assert.Null(store.GetEvidence("other-admin", "generation", windowId, storedName));
        store.RemoveEvidence("admin", "generation", windowId, storedName);
        Assert.Null(store.GetEvidence("admin", "generation", windowId, storedName));
    }

    [Fact]
    public void PreviewSession_RoutesRequestStateToMemoryAndOtherKeysToInnerSession()
    {
        var store = CreateStore(new FakeTimeProvider(Start));
        var inner = new TestSession();
        var preview = new ImpersonationPreviewSession(inner, store, "admin", "generation");
        var journeyBytes = new byte[] { 4, 5, 6 };

        preview.Set("request_window", journeyBytes);
        preview.Set("other-key", [7]);

        Assert.False(inner.TryGetValue("request_window", out _));
        Assert.True(preview.TryGetValue("request_window", out var restoredJourney));
        Assert.Equal(journeyBytes, restoredJourney);
        Assert.True(inner.TryGetValue("other-key", out var ordinaryValue));
        Assert.Equal(new byte[] { 7 }, ordinaryValue);
        Assert.Contains("request_window", preview.Keys);
    }

    [Fact]
    public void PreviewSession_ClearRemovesMemoryAndClearsInnerSession()
    {
        var store = CreateStore(new FakeTimeProvider(Start));
        var inner = new TestSession();
        var preview = new ImpersonationPreviewSession(inner, store, "admin", "generation");
        preview.Set("request_window", [1]);
        preview.Set("other-key", [2]);

        preview.Clear();

        Assert.Empty(preview.Keys);
        Assert.False(preview.TryGetValue("request_window", out _));
        Assert.False(inner.TryGetValue("other-key", out _));
    }

    private static InMemoryImpersonationPreviewStore CreateStore(TimeProvider clock) =>
        new(new MemoryCache(new MemoryCacheOptions()), clock);

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value.ToArray();
        public bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value)
        {
            if (_values.TryGetValue(key, out var found))
            {
                value = found.ToArray();
                return true;
            }

            value = null;
            return false;
        }
    }
}
