using System.Security.Claims;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Infrastructure.Authentication;
using DfE.CheckPerformanceData.Infrastructure.Impersonation;
using DfE.CheckPerformanceData.Web.Impersonation;
using DfE.CheckPerformanceData.Web.Middleware;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Impersonation;

public sealed class ImpersonationContextMiddlewareTests
{
    [Fact]
    public async Task Exit_action_clears_preview_state_through_the_middleware_session_wrapper()
    {
        var protection = new EphemeralDataProtectionProvider();
        var store = new DistributedImpersonationSessionStore(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), protection);
        var preview = new InMemoryImpersonationPreviewStore(
            new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
        var original = Substitute.For<ICurrentUserService>();
        original.UserId.Returns("11111111-1111-1111-1111-111111111111");
        original.OrganisationLaestab.Returns("860/4070");
        original.OrganisationUrn.Returns("142313");
        var session = new TestSession();
        var context = new DefaultHttpContext();
        context.Features.Set<ISessionFeature>(new SessionFeature { Session = session });
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, original.UserId), new Claim(ClaimTypes.Role, ImpersonationAccessPolicy.AdminRole)],
            "Cookies"));
        context.Items[AuthenticatedSessionBinding.ItemKey] = "authenticated-session";
        context.Request.Method = HttpMethods.Post;
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new ControllerActionDescriptor
            {
                ControllerName = "Impersonation",
                ActionName = "Exit"
            }),
            "Impersonation exit"));

        var sessionService = new ImpersonationSessionService(
            new HttpContextAccessor { HttpContext = context }, original, store, protection,
            Substitute.For<ICheckYourPupilDataRepository>(), preview);
        var binding = sessionService.ResolveBinding(context)!;
        const string generation = "active-generation";
        await store.SetAsync(binding, new(generation, new("7654321", "100002", 4, 11)), default);
        preview.Set(binding, generation, "request_window", [1]);

        var audit = Substitute.For<IImpersonationAuditWriter>();
        audit.RecordAsync(Arg.Any<ImpersonationAuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var middleware = new ImpersonationContextMiddleware(_ =>
            sessionService.SetSelectionAsync(null, CancellationToken.None));

        await middleware.InvokeAsync(context, sessionService, new NoOpSessionLock(), audit, original);

        Assert.False(preview.TryGet(binding, generation, "request_window", out _));
        Assert.Null((await store.GetAsync(binding, default))?.Selection);
    }

    private sealed class NoOpSessionLock : IImpersonationSessionLock
    {
        public Task<IAsyncDisposable> AcquireAsync(string binding, CancellationToken ct) =>
            Task.FromResult<IAsyncDisposable>(new Lease());

        private sealed class Lease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal)
        {
            [CpdSessionIdentity.IdKey] = Encoding.UTF8.GetBytes("middleware-session-id")
        };

        public bool IsAvailable => true;
        public string Id => "middleware-session";
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
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
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
