using System.Numerics;
using System.Security.Cryptography;

namespace DfE.CheckPerformanceData.Application.Search;

// A short, URL-safe stand-in for a page id, used where a link has to name pages (/search?pages=).
// It is the first eight characters of the base62 form of the SHA-256 hash of the page's id, so it
// needs no storage, never changes while the page exists, and survives the page being renamed or
// moved. It is not the page's id and cannot be turned back into one: the server finds the page by
// working out the token of every current page and matching.
//
// Eight base62 characters give about 2^47 values, so two of a few hundred pages sharing a token is
// vanishingly unlikely. If it ever happens, both pages are searched (see PageTokenIndex).
public static class PageToken
{
    public const int Length = 8;

    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const char Separator = ',';

    // A 256-bit hash needs 43 base62 digits; leading zeros are kept so every token is full length.
    private const int HashDigits = 43;

    public static string For(Guid pageId)
    {
        // Big-endian (RFC 4122) byte order, so the token matches the id as it is written.
        var hash = SHA256.HashData(pageId.ToByteArray(bigEndian: true));
        var value = new BigInteger(hash, isUnsigned: true, isBigEndian: true);

        var digits = new char[HashDigits];
        for (var i = HashDigits - 1; i >= 0; i--)
        {
            value = BigInteger.DivRem(value, Alphabet.Length, out var digit);
            digits[i] = Alphabet[(int)digit];
        }
        return new string(digits, 0, Length);
    }

    // The distinct tokens in a comma-separated list, in the order given. Blank entries are dropped;
    // anything else is kept as given; a malformed token simply never matches a page.
    public static IReadOnlyList<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var tokens = new List<string>();
        foreach (var part in raw.Split(Separator))
        {
            var token = part.Trim();
            if (token.Length > 0 && !tokens.Contains(token, StringComparer.Ordinal))
                tokens.Add(token);
        }
        return tokens;
    }

    // The canonical comma-separated form of a token list, or null when it holds none.
    public static string? Normalise(string? raw)
    {
        var tokens = ParseList(raw);
        return tokens.Count == 0 ? null : string.Join(Separator, tokens);
    }
}
