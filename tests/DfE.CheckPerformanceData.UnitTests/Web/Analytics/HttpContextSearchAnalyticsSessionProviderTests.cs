using DfE.CheckPerformanceData.Web.Analytics;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Analytics;

// The provider is what decides which session id every analytics row is filed under, and
// what the telemetry decorator reads to decide whether to write a row at all — a null means
// "skip the sink write". It reads the app-owned identity rather than ASP.NET's Session.Id
// so that the absolute-lifetime cap can rotate it; keying on the framework id would pin all
// traffic from a replayed cookie to one unbounded session.
public sealed class HttpContextSearchAnalyticsSessionProviderTests
{
    private readonly IHttpContextAccessor _accessor = Substitute.For<IHttpContextAccessor>();

    private HttpContextSearchAnalyticsSessionProvider Sut() => new(_accessor);

    [Fact]
    public void GetSessionId_NoRequestInFlight_ReturnsNull()
    {
        // Background service or hosted worker: there is no session to attribute the event
        // to, so the sink must skip rather than invent one.
        _accessor.HttpContext.Returns((HttpContext?)null);

        Assert.Null(Sut().GetSessionId());
    }

    [Fact]
    public void GetSessionId_SessionMiddlewareHasNotLoaded_ReturnsNull()
    {
        var context = new DefaultHttpContext();
        context.Session = new FakeSession { IsAvailable = false };
        _accessor.HttpContext.Returns(context);

        Assert.Null(Sut().GetSessionId());
    }

    [Fact]
    public void GetSessionId_LoadedSessionWithNoIdentityEstablished_ReturnsNull()
    {
        var context = new DefaultHttpContext();
        context.Session = new FakeSession();
        _accessor.HttpContext.Returns(context);

        Assert.Null(Sut().GetSessionId());
    }

    [Fact]
    public void GetSessionId_ReturnsTheAppOwnedIdentity_NotTheFrameworkSessionId()
    {
        // Pins the reason this indirection exists at all: Session.Id cannot rotate, the
        // app-owned identity can, and analytics must key on the one that rotates.
        var session = new FakeSession();
        session.SetString(CpdSessionIdentity.IdKey, "app-owned-identity");

        var context = new DefaultHttpContext();
        context.Session = session;
        _accessor.HttpContext.Returns(context);

        var id = Sut().GetSessionId();

        Assert.Equal("app-owned-identity", id);
        Assert.NotEqual(session.Id, id);
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsAvailable { get; set; } = true;
        public string Id => "framework-session-id";
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
