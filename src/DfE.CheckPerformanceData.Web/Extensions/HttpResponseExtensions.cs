using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Web.Extensions;

/// <summary>
/// The single mechanism that marks a response as a personal-data download (OWASP A04, #615).
///
/// Every in-scope download action calls <see cref="NoStore"/> as its first statement, so the header
/// is present not only on the file but on the redirect / Forbid / NotFound outcomes too. The value
/// lives in one constant so it can never drift, and no download sets <c>Cache-Control</c> its own
/// way (the production sweep in NoStoreDownloadSourceGuardTests enforces both).
/// </summary>
public static class HttpResponseExtensions
{
    /// <summary>The exact value every personal-data download declares.</summary>
    public const string NoStoreValue = "no-store";

    /// <summary>
    /// Guarantees the <c>no-store</c> directive is on the response's <c>Cache-Control</c> header.
    /// In the live pipeline the OIDC authentication middleware already declares
    /// <c>no-cache, no-store</c> on dynamic responses, so the on-wire value merges with any
    /// pre-existing directives (static-asset routes short-circuit before it and keep their own
    /// caching). In a bare <c>HttpContext</c> (unit tests) the result is exactly <c>no-store</c>.
    /// </summary>
    public static void NoStore(this HttpResponse response) => response.Headers.CacheControl = NoStoreValue;
}
