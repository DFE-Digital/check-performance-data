using System.Security.Cryptography;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using TokenEntity = DfE.CheckPerformance.Persistence.Entities.ShareToken;

namespace DfE.CheckPerformanceData.Persistence.Observability;

// Mints, validates and revokes the opaque tokens that gate the anonymised share link and the
// wallboard.
//
// Security design:
//  - A token is 32 bytes (256 bits, comfortably above the >=128-bit floor) drawn from
//    RandomNumberGenerator and base64url-encoded — not a Guid, not a sequence.
//  - Only the SHA-256 hash of the token is stored; the plaintext is returned once and never
//    persisted, so a database read can never reveal a live token.
//  - Validation hashes the incoming token and compares it to each candidate's stored hash with
//    CryptographicOperations.FixedTimeEquals, a constant-time compare, so a timing side-channel
//    cannot leak how many leading bytes of a guess were correct. Candidates are scoped to the
//    requested surface and to non-revoked rows.
//
// Lives in Persistence (it reads the shared DbContext directly), mirroring the read-side metrics
// query service layering; the interface lives in Application so the Web controllers depend only on
// the abstraction.
public sealed class ShareTokenService : IShareTokenService
{
    // 32 bytes = 256 bits of entropy, well above the 128-bit minimum the gate requires.
    private const int TokenByteLength = 32;

    private readonly IPortalDbContext _dbContext;

    public ShareTokenService(IPortalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> GenerateAsync(
        string label,
        ShareSurface surface,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var token = Base64UrlEncode(tokenBytes);

        _dbContext.ShareTokens.Add(new TokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = Hash(token),
            Label = label,
            Surface = surface.ToString(),
            CreatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow,
            RevokedAtUtc = null,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        // The plaintext is returned once here and never stored.
        return token;
    }

    public async Task<bool> ValidateAsync(
        string token,
        ShareSurface surface,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        var incomingHash = Hash(token);
        var incomingBytes = System.Text.Encoding.UTF8.GetBytes(incomingHash);
        var surfaceName = surface.ToString();

        // Only live (non-revoked) tokens issued for the requested surface are candidates. The
        // narrow index seek on the hash means at most one row matches, but the compare is still
        // run in constant time so the validation cost does not depend on the guessed value.
        var candidates = await _dbContext.ShareTokens
            .Where(t => t.Surface == surfaceName && t.RevokedAtUtc == null)
            .Select(t => t.TokenHash)
            .ToListAsync(cancellationToken);

        var valid = false;
        foreach (var storedHash in candidates)
        {
            var storedBytes = System.Text.Encoding.UTF8.GetBytes(storedHash);
            if (CryptographicOperations.FixedTimeEquals(incomingBytes, storedBytes))
            {
                valid = true;
                // No early return: keep comparing so the time taken does not reveal which row (if
                // any) matched.
            }
        }

        return valid;
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var token = await _dbContext.ShareTokens.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (token is null || token.RevokedAtUtc is not null)
            return;

        token.RevokedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShareTokenSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.ShareTokens
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new { t.Id, t.Label, t.Surface, t.CreatedBy, t.CreatedAtUtc, t.RevokedAtUtc })
            .ToListAsync(cancellationToken);

        return rows
            .Select(t => new ShareTokenSummary(
                t.Id,
                t.Label,
                Enum.TryParse<ShareSurface>(t.Surface, out var surface) ? surface : ShareSurface.Share,
                t.CreatedBy,
                DateTime.SpecifyKind(t.CreatedAtUtc, DateTimeKind.Utc),
                t.RevokedAtUtc is null ? null : DateTime.SpecifyKind(t.RevokedAtUtc.Value, DateTimeKind.Utc)))
            .ToList();
    }

    private static string Hash(string token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
