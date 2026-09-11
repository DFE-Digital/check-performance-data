using System.Security.Claims;
using System.Text;
using DfE.CheckPerformanceData.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.UnitTests.Infrastructure;

// The ticket store keeps the authentication ticket server-side so the cookie carries only a key.
// See DistributedCacheTicketStore for why that matters at the ingress.
public sealed class DistributedCacheTicketStoreTests
{
    [Fact]
    public async Task StoreAsync_ReturnsAKeyThatRetrievesTheTicket()
    {
        var store = NewStore();
        var ticket = NewTicket("someone@example.gov.uk");

        var key = await store.StoreAsync(ticket);
        var retrieved = await store.RetrieveAsync(key);

        Assert.NotNull(retrieved);
        Assert.Equal(
            "someone@example.gov.uk",
            retrieved!.Principal.FindFirst(ClaimTypes.Email)?.Value);
    }

    [Fact]
    public async Task StoreAsync_IssuesADifferentKeyEveryTime()
    {
        var store = NewStore();

        var keys = new HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            keys.Add(await store.StoreAsync(NewTicket($"user{i}@example.gov.uk")));
        }

        Assert.Equal(50, keys.Count);
    }

    // The key is the whole credential once it is out of the cookie's protection, so it has to be
    // long and random rather than sequential or time-derived.
    [Fact]
    public async Task StoreAsync_IssuesAKeyWithMeaningfulEntropy()
    {
        var store = NewStore();

        var key = await store.StoreAsync(NewTicket("someone@example.gov.uk"));
        var identifier = key[(key.IndexOf(':') + 1)..];

        Assert.True(
            identifier.Length >= 32,
            $"Session key identifier '{identifier}' is only {identifier.Length} characters.");
    }

    // The cache is shared with MVC session state in the same table, so ticket keys have to sit in
    // their own namespace or the two can collide.
    [Fact]
    public async Task StoreAsync_NamespacesTheKey()
    {
        var store = NewStore();

        var key = await store.StoreAsync(NewTicket("someone@example.gov.uk"));

        Assert.StartsWith("auth-ticket:", key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenewAsync_ReplacesTheTicketUnderTheSameKey()
    {
        var store = NewStore();
        var key = await store.StoreAsync(NewTicket("before@example.gov.uk"));

        await store.RenewAsync(key, NewTicket("after@example.gov.uk"));
        var retrieved = await store.RetrieveAsync(key);

        Assert.Equal(
            "after@example.gov.uk",
            retrieved!.Principal.FindFirst(ClaimTypes.Email)?.Value);
    }

    [Fact]
    public async Task RemoveAsync_DropsTheTicket()
    {
        var store = NewStore();
        var key = await store.StoreAsync(NewTicket("someone@example.gov.uk"));

        await store.RemoveAsync(key);

        Assert.Null(await store.RetrieveAsync(key));
    }

    // A key that never existed, or whose entry has expired, must read as "not signed in" rather
    // than throw — the caller is the cookie handler on an ordinary request.
    [Fact]
    public async Task RetrieveAsync_ReturnsNullForAnUnknownKey()
    {
        var store = NewStore();

        Assert.Null(await store.RetrieveAsync("auth-ticket:does-not-exist"));
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsNullForAKeyHoldingUnreadableBytes()
    {
        var cache = NewCache();
        var store = NewStore(cache);
        await cache.SetAsync("auth-ticket:corrupt", [0x01, 0x02, 0x03]);

        Assert.Null(await store.RetrieveAsync("auth-ticket:corrupt"));
    }

    // The entry has to outlive the cookie presenting it, and the ticket's own absolute expiry is
    // the authority on when that is.
    //
    // Asserted against the options the store hands the cache rather than by waiting for a short
    // expiry to elapse: this host steps its wall clock backwards by a second or two, so any
    // assertion resting on a sub-five-second UtcNow margin fails intermittently for no product
    // reason. Reading back the requested expiry tests the same contract and cannot drift.
    [Fact]
    public async Task StoreAsync_ExpiresTheEntryWithTheTicket()
    {
        var cache = NewCache();
        var store = NewStore(cache);
        var ticket = NewTicket("someone@example.gov.uk");
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30);

        var key = await store.StoreAsync(ticket);

        // Compared against the ticket's own value rather than the local assigned to it:
        // AuthenticationProperties round-trips ExpiresUtc through an RFC1123 string, so what comes
        // back out is truncated to the second.
        Assert.Equal(ticket.Properties.ExpiresUtc, cache.LastOptionsFor(key)?.AbsoluteExpiration);
    }

    // Sliding expiration renews the ticket rather than reissuing it, so the stored entry has to
    // follow the new expiry — otherwise an active session is evicted at its original deadline.
    [Fact]
    public async Task RenewAsync_MovesTheEntryExpiryWithTheTicket()
    {
        var cache = NewCache();
        var store = NewStore(cache);
        var key = await store.StoreAsync(NewTicket("someone@example.gov.uk"));

        var renewed = NewTicket("someone@example.gov.uk");
        renewed.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(3);
        await store.RenewAsync(key, renewed);

        Assert.Equal(renewed.Properties.ExpiresUtc, cache.LastOptionsFor(key)?.AbsoluteExpiration);
    }

    // Without ExpiresUtc the store must still bound the entry, or a ticket with no expiry would
    // sit in the cache table forever.
    [Fact]
    public async Task StoreAsync_BoundsAnEntryWithNoExpiryOfItsOwn()
    {
        var cache = NewCache();
        var store = NewStore(cache);
        var ticket = NewTicket("someone@example.gov.uk");
        ticket.Properties.ExpiresUtc = null;

        var key = await store.StoreAsync(ticket);

        Assert.NotNull(await store.RetrieveAsync(key));
        Assert.NotNull(cache.LastOptionsFor(key)?.AbsoluteExpirationRelativeToNow);
    }

    // Moving the ticket out of the cookie moves the id, access and refresh tokens into a database
    // table that is inside the backups. Written in the clear that would put replayable credentials
    // into every backup copy, so the payload is encrypted at rest.
    [Fact]
    public async Task StoreAsync_DoesNotWriteTheTicketToTheCacheInTheClear()
    {
        var cache = NewCache();
        var store = NewStore(cache);
        var ticket = NewTicket("someone@example.gov.uk");
        ticket.Properties.StoreTokens([
            new AuthenticationToken { Name = "access_token", Value = "a-replayable-access-token" },
        ]);

        var key = await store.StoreAsync(ticket);
        var stored = await cache.GetAsync(key);

        Assert.NotNull(stored);
        Assert.DoesNotContain("a-replayable-access-token", Encoding.UTF8.GetString(stored!));
        Assert.DoesNotContain("someone@example.gov.uk", Encoding.UTF8.GetString(stored!));
    }

    // Encrypted at rest is only useful if it still round-trips — including the tokens the OIDC
    // handler needs for the end-session id_token_hint on sign-out.
    [Fact]
    public async Task RetrieveAsync_RestoresTheStoredTokens()
    {
        var store = NewStore();
        var ticket = NewTicket("someone@example.gov.uk");
        ticket.Properties.StoreTokens([
            new AuthenticationToken { Name = "id_token", Value = "the-id-token" },
        ]);

        var key = await store.StoreAsync(ticket);
        var retrieved = await store.RetrieveAsync(key);

        Assert.Equal("the-id-token", retrieved!.Properties.GetTokenValue("id_token"));
    }

    // A ticket protected under a different key ring must read as "not signed in", not as an
    // exception on an ordinary page load.
    [Fact]
    public async Task RetrieveAsync_ReturnsNullWhenTheTicketCannotBeUnprotected()
    {
        var cache = NewCache();
        var key = await NewStore(cache).StoreAsync(NewTicket("someone@example.gov.uk"));

        var underADifferentKeyRing = NewStore(cache, new EphemeralDataProtectionProvider());

        Assert.Null(await underADifferentKeyRing.RetrieveAsync(key));
    }

    // A ticket that cannot be read is a silent mass sign-out: after a key-ring change every user
    // is bounced to DfE Sign-in on every request. Logged so an administrator has something to see
    // in the application log, and dropped so it cannot be retried until it expires on its own.
    [Fact]
    public async Task RetrieveAsync_LogsAndDiscardsATicketItCannotRead()
    {
        var cache = NewCache();
        var log = new CapturingLogger();
        var key = await NewStore(cache).StoreAsync(NewTicket("someone@example.gov.uk"));

        var underADifferentKeyRing = NewStore(cache, new EphemeralDataProtectionProvider(), log);
        var result = await underADifferentKeyRing.RetrieveAsync(key);

        Assert.Null(result);
        Assert.Contains(log.Warnings, w => w.Contains("could not be read", StringComparison.Ordinal));
        Assert.Null(await cache.GetAsync(key));
    }

    // The cookie handler reuses the session key it read from the request, so a sign-in that
    // happens while a usable ticket is present rebinds that key to the new user. That is the
    // shape of session fixation and does not occur in ordinary use, so it is recorded.
    [Fact]
    public async Task RenewAsync_LogsWhenTheSessionIsReboundToADifferentUser()
    {
        var log = new CapturingLogger();
        var store = NewStore(logger: log);
        var key = await store.StoreAsync(NewTicket("attacker@example.gov.uk"));

        await store.RenewAsync(key, NewTicket("victim@example.gov.uk"));

        Assert.Contains(log.Warnings, w => w.Contains("different user", StringComparison.Ordinal));
    }

    // Sign-out removes the row. A request already in flight in another tab would otherwise renew
    // it straight back, undoing the sign-out and restoring access to any copy of that cookie.
    [Fact]
    public async Task RenewAsync_DoesNotResurrectATicketThatHasBeenRemoved()
    {
        var store = NewStore();
        var key = await store.StoreAsync(NewTicket("someone@example.gov.uk"));
        await store.RemoveAsync(key);

        await store.RenewAsync(key, NewTicket("someone@example.gov.uk"));

        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task RenewAsync_IsSilentForAnOrdinarySlidingRenewal()
    {
        var log = new CapturingLogger();
        var store = NewStore(logger: log);
        var key = await store.StoreAsync(NewTicket("someone@example.gov.uk"));

        await store.RenewAsync(key, NewTicket("someone@example.gov.uk"));

        Assert.Empty(log.Warnings);
    }

    // The PostgreSQL cache rejects an absolute expiry that is not in the future; the in-memory one
    // accepts it silently. Clamping here keeps that difference out of production.
    [Fact]
    public async Task StoreAsync_ClampsAnExpiryThatHasAlreadyPassed()
    {
        var cache = NewCache();
        var store = NewStore(cache);
        var ticket = NewTicket("someone@example.gov.uk");
        ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

        var key = await store.StoreAsync(ticket);

        var options = cache.LastOptionsFor(key);
        Assert.Null(options?.AbsoluteExpiration);
        Assert.NotNull(options?.AbsoluteExpirationRelativeToNow);
    }

    private static AuthenticationTicket NewTicket(string email)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, email), new Claim(ClaimTypes.NameIdentifier, email)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) },
            CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static RecordingCache NewCache() => new();

    private static DistributedCacheTicketStore NewStore(
        IDistributedCache? cache = null,
        IDataProtectionProvider? dataProtection = null,
        ILogger<DistributedCacheTicketStore>? logger = null) =>
        new(cache ?? NewCache(),
            dataProtection ?? SharedDataProtection,
            logger ?? NullLogger<DistributedCacheTicketStore>.Instance);

    // One provider across a test's stores, so two stores in the same test can read each other's
    // tickets exactly as two pods sharing the persisted key ring do.
    private static readonly IDataProtectionProvider SharedDataProtection =
        new EphemeralDataProtectionProvider();

    // Captures warning-level messages so the log-on-failure behaviour can be asserted rather
    // than taken on trust. The real sink is an ILoggerProvider, so anything written here reaches
    // the admin application-log surface in the running app.
    private sealed class CapturingLogger : ILogger<DistributedCacheTicketStore>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }

    // MemoryDistributedCache with the entry options captured, so the expiry assertions can look at
    // what the store actually asked for rather than only at what survived.
    private sealed class RecordingCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(
            Options.Create(new MemoryDistributedCacheOptions()));

        private readonly Dictionary<string, DistributedCacheEntryOptions> _options = [];

        public DistributedCacheEntryOptions? LastOptionsFor(string key) =>
            _options.TryGetValue(key, out var o) ? o : null;

        public byte[]? Get(string key) => _inner.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            _inner.GetAsync(key, token);
        public void Refresh(string key) => _inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) =>
            _inner.RefreshAsync(key, token);
        public void Remove(string key) => _inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) =>
            _inner.RemoveAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _options[key] = options;
            _inner.Set(key, value, options);
        }

        public Task SetAsync(
            string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            _options[key] = options;
            return _inner.SetAsync(key, value, options, token);
        }
    }
}
