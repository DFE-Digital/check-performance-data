using System.Security.Cryptography;
using System.Text;

namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// Signs and checks the settings of a resize or crop URL. The signature is an HMAC-SHA256 of the
/// image id, its version and the canonical settings (<see cref="MediaRenditionQuery.Canonical"/>),
/// cut to 128 bits and written in URL-safe base64. The slug and extension of the URL are not
/// signed: they do not change what is served.
///
/// Only the server makes these URLs, so a visitor cannot ask for any rendition the site did not
/// link to. A signature never changes for the same input, so a page that is rendered again keeps
/// the same URLs and browsers keep their cached copies.
/// </summary>
public sealed class MediaUrlSigner(IMediaUrlSigningKey key)
{
    private const int SignatureBytes = 16;

    public string Sign(Guid assetId, int version, MediaRendition rendition) =>
        Base64UrlEncode(Compute(assetId, version, rendition));

    public bool IsValid(Guid assetId, int version, MediaRendition rendition, string? signature)
    {
        if (string.IsNullOrEmpty(signature) || !TryBase64UrlDecode(signature, out var given)) return false;
        return CryptographicOperations.FixedTimeEquals(given, Compute(assetId, version, rendition));
    }

    private byte[] Compute(Guid assetId, int version, MediaRendition rendition)
    {
        var message = Encoding.UTF8.GetBytes($"{assetId:D}/{version}?{MediaRenditionQuery.Canonical(rendition)}");
        return HMACSHA256.HashData(key.GetKey(), message)[..SignatureBytes];
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64UrlDecode(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length > 64 || !text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return false;
        var padding = (text.Length % 4) switch { 0 => "", 2 => "==", 3 => "=", _ => null };
        if (padding is null) return false;
        var padded = text.Replace('-', '+').Replace('_', '/') + padding;
        var buffer = new byte[padded.Length];
        if (!Convert.TryFromBase64String(padded, buffer, out var written)) return false;
        bytes = buffer[..written];
        return true;
    }
}
