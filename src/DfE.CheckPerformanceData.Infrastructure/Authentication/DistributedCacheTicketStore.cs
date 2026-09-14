using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Infrastructure.Authentication;

// Keeps the authentication ticket on the server and gives the browser a key instead.
//
// Without this the whole ticket is written into .AspNetCore.Cookies: the organisation blob the
// userinfo endpoint returns, a claim per granted role, and — because SaveTokens is on — the id,
// access and refresh tokens. Encrypted and base64-encoded that came to roughly 4.2 KB, past a
// single cookie's 4,090-byte ceiling, so ChunkingCookieManager split it across C1 and C2.
//
// Cookie size is an availability concern here, not a tidiness one. The deployed ingress caps a
// single request header field at one 8k buffer, and every cookie for a host is concatenated into
// one Cookie field. nginx rejects an oversized one with 400 before the request reaches the pod,
// so the application cannot log it, cannot explain it, and cannot expire the cookie that caused
// it — every page stays broken until the user clears their cookies by hand. The auth ticket was
// consuming over half that budget on its own and grew with every claim the identity provider
// added.
//
// Holding it here makes the cookie a short opaque key whose length has nothing to do with the
// principal, so the budget stops moving. The tokens stay on the ticket, which matters: the
// end-session request built by the OIDC handler on sign-out uses the stored id_token as its
// id_token_hint, so nothing about the sign-out flow changes.
//
// Backed by IDistributedCache, which the application already points at PostgreSQL and shares
// across every replica (see the session-store wiring), so a request that load-balances to another
// pod resolves the same ticket. The trade is a cache read per authenticated request — alongside
// the session read already happening — and that clearing the cache table signs everyone out.
//
// The stored payload is encrypted rather than written as bare serialised bytes. In the cookie the
// ticket was protected in transit as a matter of course; moving it into a table would otherwise
// downgrade that, because the ticket carries the id, access and refresh tokens and that table is
// inside the database backups. Encrypting keeps a backup, a replica or a stray query from yielding
// credentials that can be replayed against the identity provider. It uses the same shared,
// persisted key ring as the rest of the application, so every replica can read what any other
// wrote — and if the key ring is ever rotated out, tickets simply fail to unprotect and users sign
// in again, which is how an unreadable ticket already behaves.
//
// Nothing here logs the session key. The key is the entire credential — an administrator reading
// the application log must not come away able to impersonate the session the entry is about.
public sealed class DistributedCacheTicketStore : ITicketStore
{
    // Ticket entries share a cache (and a table) with MVC session state, so they carry their own
    // prefix rather than trusting two key spaces not to meet.
    internal const string KeyPrefix = "auth-ticket:";

    private readonly IDistributedCache _cache;
    private readonly IDataProtector _protector;
    private readonly ILogger<DistributedCacheTicketStore> _logger;

    public DistributedCacheTicketStore(
        IDistributedCache cache,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<DistributedCacheTicketStore> logger)
    {
        _cache = cache;
        _logger = logger;

        // Versioned purpose string: changing it invalidates every existing ticket rather than
        // risking a payload being interpreted under a different shape.
        _protector = dataProtectionProvider.CreateProtector(
            "DfE.CheckPerformanceData.Infrastructure.Authentication.DistributedCacheTicketStore.v1");
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket) =>
        StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var key = KeyPrefix + NewSessionId();
        await WriteAsync(key, ticket, cancellationToken);
        return key;
    }

    public Task<string> StoreAsync(
        AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) =>
        StoreAsync(ticket, cancellationToken);

    public Task RenewAsync(string key, AuthenticationTicket ticket) =>
        RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        // The cookie handler reuses the session key it read from the request, so this is called
        // both for an ordinary sliding renewal and for a sign-in that happened while a usable
        // ticket was already present. In the second case the key is being rebound to whoever has
        // just authenticated — which is how session fixation works if somebody managed to plant
        // the cookie first. The store cannot rotate the key (the handler has already decided
        // which one the cookie will carry), so it records the event instead: a rebind is not
        // something that happens in ordinary use, and an administrator seeing these has something
        // concrete to act on. The request's user and path are attached by the log sink.
        var existing = await RetrieveAsync(key, cancellationToken);

        if (existing is null)
        {
            // The row has gone since it was read earlier in this request — the usual cause is a
            // sign-out from another tab, or a peer evicting it. Writing the ticket back would
            // recreate a session the user has just ended, and would hand a stolen cookie its
            // access back. Doing nothing is safe here: the handler only reaches a renewal after a
            // successful read, and when it signs a user in over a missing row it mints a fresh key
            // through StoreAsync instead of coming through here (verified against the real cookie
            // handler — sign-in still succeeds with the row deleted).
            return;
        }

        var existingSubject = SubjectOf(existing);
        var incomingSubject = SubjectOf(ticket);

        if (existingSubject is not null
            && incomingSubject is not null
            && !string.Equals(existingSubject, incomingSubject, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Authentication ticket renewed onto a session that belonged to a different user. "
                + "This is the shape of a session-fixation attempt: the signing-in user arrived "
                + "holding a session key that was already in use. Treat repeated entries as "
                + "suspicious.");
        }

        await WriteAsync(key, ticket, cancellationToken);
    }

    public Task RenewAsync(
        string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken) =>
        RenewAsync(key, ticket, cancellationToken);

    public Task<AuthenticationTicket?> RetrieveAsync(string key) =>
        RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        // Deliberately outside the try below: a cache that is unreachable is a fault worth
        // surfacing, not a reason to quietly sign every user out mid-incident.
        var bytes = await _cache.GetAsync(key, cancellationToken);
        if (bytes is null or { Length: 0 }) return null;

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(bytes));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or InvalidOperationException
                                      or ArgumentException or IndexOutOfRangeException)
        {
            // A key that resolves to bytes this build cannot read is indistinguishable, from the
            // caller's point of view, from a key that resolved to nothing: either way there is no
            // usable identity. Returning null signs the user out and sends them back through
            // DfE Sign-in, where throwing would surface a 500 on an ordinary page load.
            //
            // It is logged because the alternative is invisible: a key-ring problem — blob
            // storage permissions, a revoked key, a changed application name — signs every user
            // out on every request, and without this an administrator sees only a wave of
            // sign-in redirects with nothing explaining them. The row is dropped too, so it
            // cannot be retried until it expires of its own accord.
            _logger.LogWarning(
                ex,
                "Stored authentication ticket could not be read ({ExceptionType}) and has been "
                + "discarded; the user will be asked to sign in again. A burst of these usually "
                + "means the data-protection key ring has changed or become unreadable.",
                ex.GetType().Name);

            await RemoveAsync(key, cancellationToken);
            return null;
        }
    }

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key, HttpContext httpContext, CancellationToken cancellationToken) =>
        RetrieveAsync(key, cancellationToken);

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public Task RemoveAsync(string key, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(key, cancellationToken);

    public Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken) =>
        RemoveAsync(key, cancellationToken);

    private Task WriteAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken) =>
        _cache.SetAsync(
            key,
            _protector.Protect(TicketSerializer.Default.Serialize(ticket)),
            ExpiryFor(ticket),
            cancellationToken);

    private static string? SubjectOf(AuthenticationTicket? ticket) =>
        ticket?.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    // The entry has to outlive the cookie presenting it, and no longer. The ticket's own
    // ExpiresUtc is the authority — the cookie handler refreshes it on every renewal under
    // sliding expiration, so the entry slides with it. A ticket carrying no expiry at all still
    // has to be bounded, or its row would sit in the cache table indefinitely.
    //
    // An expiry already in the past is clamped rather than passed through. The cookie handler
    // does not produce one today, but the PostgreSQL cache rejects it outright where the
    // in-memory one accepts it silently — so without this the difference between the two would
    // only show up in production, as a failed sign-in rather than a failed test.
    private DistributedCacheEntryOptions ExpiryFor(AuthenticationTicket ticket)
    {
        if (ticket.Properties.ExpiresUtc is not { } expiresUtc)
        {
            return new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = FallbackLifetime
            };
        }

        return expiresUtc <= DateTimeOffset.UtcNow
            ? new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = MinimumLifetime }
            : new DistributedCacheEntryOptions { AbsoluteExpiration = expiresUtc };
    }

    // Long enough for the write to be accepted, short enough that an already-expired ticket is
    // gone almost immediately. The handler's own expiry check rejects it in the meantime.
    private static readonly TimeSpan MinimumLifetime = TimeSpan.FromSeconds(1);

    // Only reached by a ticket carrying no ExpiresUtc of its own, which the cookie handler does
    // not produce — a backstop against an unbounded row rather than a lifetime to rely on. A
    // constant rather than a setting: nothing should ever need to tune it at runtime, and making
    // it bindable would force every caller of the registration to have configuration wired up.
    private static readonly TimeSpan FallbackLifetime = TimeSpan.FromHours(12);

    // The session id is the entire credential once it is outside the cookie's encryption, so it
    // comes from the cryptographic RNG rather than from Guid.NewGuid or anything time-derived.
    // 256 bits, base64url so it is safe in a cache key.
    private static string NewSessionId() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}
