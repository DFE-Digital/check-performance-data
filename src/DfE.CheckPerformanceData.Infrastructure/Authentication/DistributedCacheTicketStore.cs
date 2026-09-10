using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

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
public sealed class DistributedCacheTicketStore : ITicketStore
{
    // Ticket entries share a cache (and a table) with MVC session state, so they carry their own
    // prefix rather than trusting two key spaces not to meet.
    internal const string KeyPrefix = "auth-ticket:";

    private readonly IDistributedCache _cache;
    private readonly AuthenticationTicketStoreOptions _options;
    private readonly IDataProtector _protector;

    public DistributedCacheTicketStore(
        IDistributedCache cache,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<AuthenticationTicketStoreOptions> options)
    {
        _cache = cache;
        _options = options.Value;

        // Versioned purpose string: changing it invalidates every existing ticket rather than
        // risking a payload being interpreted under a different shape.
        _protector = dataProtectionProvider.CreateProtector(
            "DfE.CheckPerformanceData.Infrastructure.Authentication.DistributedCacheTicketStore.v1");
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = KeyPrefix + NewSessionId();
        await WriteAsync(key, ticket);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => WriteAsync(key, ticket);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        // Deliberately outside the try below: a cache that is unreachable is a fault worth
        // surfacing, not a reason to quietly sign every user out mid-incident.
        var bytes = await _cache.GetAsync(key);
        if (bytes is null or { Length: 0 }) return null;

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(bytes));
        }
        catch (Exception)
        {
            // A key that resolves to bytes this build cannot read is indistinguishable, from the
            // caller's point of view, from a key that resolved to nothing: either way there is no
            // usable identity. Returning null signs the user out and sends them back through
            // DfE Sign-in, where throwing would surface a 500 on an ordinary page load. Reachable
            // whenever a ticket outlives a key-ring rotation or a change to its serialised shape.
            return null;
        }
    }

    public Task RemoveAsync(string key) => _cache.RemoveAsync(key);

    private Task WriteAsync(string key, AuthenticationTicket ticket) =>
        _cache.SetAsync(
            key,
            _protector.Protect(TicketSerializer.Default.Serialize(ticket)),
            ExpiryFor(ticket));

    // The entry has to outlive the cookie presenting it, and no longer. The ticket's own
    // ExpiresUtc is the authority — the cookie handler refreshes it on every renewal under
    // sliding expiration, so the entry slides with it. A ticket carrying no expiry at all still
    // has to be bounded, or its row would sit in the cache table indefinitely.
    private DistributedCacheEntryOptions ExpiryFor(AuthenticationTicket ticket) =>
        ticket.Properties.ExpiresUtc is { } expiresUtc
            ? new DistributedCacheEntryOptions { AbsoluteExpiration = expiresUtc }
            : new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.FallbackLifetime
            };

    // The session id is the entire credential once it is outside the cookie's encryption, so it
    // comes from the cryptographic RNG rather than from Guid.NewGuid or anything time-derived.
    // 256 bits, base64url so it is safe in a cache key.
    private static string NewSessionId() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
