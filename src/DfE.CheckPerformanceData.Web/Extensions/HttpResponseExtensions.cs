using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Extensions;

/// <summary>
/// The single mechanism that marks a response as a personal-data download (OWASP A04, #615).
///
/// Every action marked <see cref="RequiresNoStoreAttribute"/> calls <see cref="NoStore"/> before any
/// early return, so the header is present on the file, the redirect and the NotFound outcomes too.
/// The value lives in one constant so it can never drift, and no other code writes the literal
/// (both enforced by NoStoreDownloadSourceGuardTests).
/// </summary>
public static class HttpResponseExtensions
{
    /// <summary>The exact value every personal-data download declares.</summary>
    public const string NoStoreValue = "no-store";

    /// <summary>
    /// Sets <c>Cache-Control: no-store</c>, replacing whatever value was on the response already.
    /// <c>no-store</c> alone satisfies the requirement, so this deliberately does not add
    /// <c>no-cache</c> and does not append to existing directives. A later component may re-declare
    /// the header (the local Docker check observed the final on-wire value <c>no-cache,no-store</c>
    /// on dynamic responses); that is left alone, because it is stricter, never weaker.
    /// </summary>
    public static void NoStore(this HttpResponse response) => response.Headers.CacheControl = NoStoreValue;
}
