namespace DfE.CheckPerformanceData.Infrastructure.Authentication;

// Settings for the server-side authentication ticket store.
public sealed class AuthenticationTicketStoreOptions
{
    // Used only for a ticket that arrives with no ExpiresUtc of its own. The cookie handler always
    // sets one from ExpireTimeSpan, so this is a backstop against an unbounded cache entry rather
    // than a lifetime anyone should be relying on — deliberately generous enough not to sign a
    // legitimate user out early, and short enough that an orphan cannot linger.
    public TimeSpan FallbackLifetime { get; set; } = TimeSpan.FromHours(12);
}
