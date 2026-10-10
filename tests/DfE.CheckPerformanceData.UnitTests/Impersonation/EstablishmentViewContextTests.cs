using System.Security.Claims;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Infrastructure.Authentication;
using DfE.CheckPerformanceData.Infrastructure.Impersonation;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Impersonation;

public sealed class EstablishmentViewContextTests
{
    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
    private readonly ICurrentUserService _original = Substitute.For<ICurrentUserService>();
    private readonly ICheckYourPupilDataRepository _pupils = Substitute.For<ICheckYourPupilDataRepository>();
    private readonly IImpersonationSessionStore _store;
    public EstablishmentViewContextTests()
    {
        _original.UserId.Returns("original-admin");
        _original.OrganisationUrn.Returns("100001");
        _original.OrganisationLaestab.Returns("1234567");
        _store = new DistributedImpersonationSessionStore(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), _protection);
    }
    private ImpersonationSessionService Create(HttpContext? context = null) => new(
        new HttpContextAccessor { HttpContext = context ?? new DefaultHttpContext { Session = new TestSession() } },
        _original, _store, _protection, _pupils);

    [Fact]
    public async Task Selection_changes_reads_but_never_original_login_authority()
    {
        var view = Create();
        await view.InitialiseAsync("session-a", default);
        Assert.Equal("1234567", view.OrganisationLaestab);
        await view.SetSelectionAsync(new("7654321", "100002", 11, 18), default);
        Assert.True(view.IsImpersonating);
        Assert.Equal("7654321", view.OrganisationLaestab);
        Assert.Equal("100002", view.OrganisationUrn);
        Assert.Equal(11, view.LowestAge);
        Assert.Equal("1234567", _original.OrganisationLaestab);
        Assert.Equal("100001", _original.OrganisationUrn);
        await Assert.ThrowsAsync<ImpersonationWriteDeniedException>(() => view.EnsureCanWriteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Exit_rotates_stamps_and_clears_journey_state_across_requests()
    {
        var context = new DefaultHttpContext { Session = new TestSession() };
        context.Session.SetString("request_window", "old journey");
        context.Session.SetString("SelectedWindowId", "old window");
        context.Session.SetString("bulk_selection_window", "old selection");
        var first = Create(context);
        await first.InitialiseAsync("session-a", default);
        var originalStamp = first.CreateStamp();
        await first.SetSelectionAsync(new("7654321", "100002", 11, 18), default);
        Assert.False(first.ValidStamp(originalStamp));
        Assert.Empty(context.Session.Keys);
        var viewingStamp = first.CreateStamp();
        var next = Create();
        await next.InitialiseAsync("session-a", default);
        Assert.True(next.IsImpersonating);
        Assert.True(next.ValidStamp(viewingStamp));
        await next.SetSelectionAsync(null, default);
        var afterExit = Create();
        await afterExit.InitialiseAsync("session-a", default);
        Assert.False(afterExit.IsImpersonating);
        Assert.False(afterExit.ValidStamp(viewingStamp));
        Assert.False(afterExit.ValidStamp(originalStamp));
        Assert.Equal("100001", afterExit.OrganisationUrn);
    }

    [Fact]
    public async Task Separate_sessions_and_forged_stamps_cannot_reuse_selection()
    {
        var first = Create();
        await first.InitialiseAsync("session-a", default);
        await first.SetSelectionAsync(new("7654321", "100002", 3, 11), default);
        var second = Create();
        await second.InitialiseAsync("session-b", default);
        Assert.False(second.IsImpersonating);
        Assert.False(second.ValidStamp(first.CreateStamp()));
        Assert.False(second.ValidStamp("not-a-protected-context"));
        await Assert.ThrowsAsync<ImpersonationWriteDeniedException>(() => second.EnsureCanWriteAsync(Guid.NewGuid()));
    }

    [Fact]
    public void Binding_requires_validated_authentication_and_rotates_with_session_identity()
    {
        var context = new DefaultHttpContext { Session = new TestSession() };
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "original-admin")], "Cookies"));
        var view = Create(context);
        Assert.Null(view.ResolveBinding(context));
        var assetRequest = new DefaultHttpContext { User = context.User };
        assetRequest.Items[AuthenticatedSessionBinding.ItemKey] = "synthetic-validated-binding";
        Assert.Null(view.ResolveBinding(assetRequest));
        context.Items[AuthenticatedSessionBinding.ItemKey] = "synthetic-validated-binding";
        var before = view.ResolveBinding(context);
        Assert.NotNull(before);
        context.Session.Clear();
        Assert.NotEqual(before, view.ResolveBinding(context));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Both_original_roles_are_required(bool admin, bool impersonation, bool allowed)
    {
        var claims = new List<Claim>();
        if (admin) claims.Add(new(ClaimTypes.Role, "cypmd_admin"));
        if (impersonation) claims.Add(new(ClaimTypes.Role, "cypmd_impersonation"));
        Assert.Equal(allowed, ImpersonationAccessPolicy.CanAccess(new(new ClaimsIdentity(claims, "Cookies"))));
        Assert.False(ImpersonationAccessPolicy.CanAccess(new(new ClaimsIdentity(claims, "DevImpersonation"))));
        Assert.False(ImpersonationAccessPolicy.CanAccess(new(new ClaimsIdentity(claims))));
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _values.Keys;
        public void Clear() => _values.Clear();
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[]? value) => _values.TryGetValue(key, out value);
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
