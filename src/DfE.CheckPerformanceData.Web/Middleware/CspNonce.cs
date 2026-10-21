using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace DfE.CheckPerformanceData.Web.Middleware;

/// <summary>
/// The per-request Content-Security-Policy nonce (#611).
/// </summary>
/// <remarks>
/// The middleware mints one nonce per response and publishes it in <c>script-src</c>; the
/// <see cref="TagHelpers.CspNonceTagHelper"/> copies it onto every script the service emits.
/// Writer and reader share <see cref="ItemKey"/> through this type so they cannot drift.
/// The value lives only on <c>HttpContext.Items</c>, in the response header and in the matching
/// <c>nonce</c> attributes: it is never logged or reused (FR-016).
/// </remarks>
public static class CspNonce
{
    /// <summary>Key under which the response's nonce is stored on <c>HttpContext.Items</c>.</summary>
    public const string ItemKey = "cpd-csp-nonce";

    /// <summary>
    /// Returns 128 bits of OS randomness, base64url-encoded without padding. Base64url is both a
    /// valid CSP nonce and safe inside an HTML attribute value, so the same string can be used in
    /// the header and the attribute.
    /// </summary>
    public static string Create() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// Reads the nonce the middleware minted for this request, or <c>null</c> when no
    /// security-headers middleware ran (unit tests, static rendering).
    /// </summary>
    public static string? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
}
