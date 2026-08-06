using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Session;

// The analytics / support-quotable session identifier.
//
// Deliberately NOT ASP.NET's Session.Id. That value is derived from the session cookie
// and the framework exposes no way to regenerate it, so it survives Session.Clear() and
// pins a browser to one identifier for as long as it keeps replaying the same cookie —
// which would let a single "session" accumulate unbounded analytics traffic and would
// make the documented absolute-lifetime cutoff invisible downstream.
//
// Holding our own id INSIDE the session store gets rotation for free: the absolute-
// lifetime wipe discards the key along with everything else, and the next Ensure mints
// a fresh one. No cookie surgery, no framework internals.
public static class CpdSessionIdentity
{
    // Public so a stand-in ISession can seed a known identity without reaching for a
    // magic string. This class stays the single source of truth for the key name.
    public const string IdKey = "_cpdSessionId";

    // Mints and stores an id when the session doesn't have one yet, otherwise returns the
    // stored value. The SetString is also what materialises the ASP.NET session cookie on
    // the response — the framework lazy-writes the cookie on first store mutation, so a
    // read-only pass would never establish one.
    public static string Ensure(ISession session)
    {
        var existing = session.GetString(IdKey);
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        var minted = Guid.NewGuid().ToString("N");
        session.SetString(IdKey, minted);
        return minted;
    }

    // Reads the id without minting one. Returns null when there is no request in flight,
    // when the session middleware has not loaded yet, or before the lifetime middleware
    // has established an identity — callers treat all three as "nothing to attribute".
    public static string? Peek(ISession? session)
    {
        if (session is null || !session.IsAvailable)
        {
            return null;
        }

        var value = session.GetString(IdKey);
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
