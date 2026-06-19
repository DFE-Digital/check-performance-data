namespace DfE.CheckPerformanceData.Application.Observability;

// The surface an opaque share token grants read-only access to. A token is scoped to exactly one
// surface so a wallboard token cannot be replayed against the share link and vice versa.
public enum ShareSurface
{
    Share,
    Wallboard,
}

// Mints, validates and revokes the opaque tokens that gate the anonymised share link and the
// wallboard. The token is the single deliberate gate for those surfaces: it does not require the
// admin role to VIEW (so a stakeholder can be sent a link) yet it is unguessable and revocable.
//
// Security contract:
//  - GenerateAsync mints a cryptographically random opaque token (>=128 bits via
//    RandomNumberGenerator) and returns the plaintext ONCE; only its SHA-256 hash is persisted,
//    so a database read can never reveal a live token.
//  - ValidateAsync hashes the incoming token and compares it to a non-revoked row with a
//    constant-time compare (CryptographicOperations.FixedTimeEquals), returning true only for a
//    live token issued for the requested surface.
//  - RevokeAsync marks a token revoked so it immediately stops validating.
//
// Implemented in the Persistence layer (it reads the shared DbContext directly), the same layering
// the read-side metrics query service uses; the interface lives here so the Web controllers depend
// only on the Application abstraction.
public interface IShareTokenService
{
    Task<string> GenerateAsync(
        string label,
        ShareSurface surface,
        string createdBy,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateAsync(
        string token,
        ShareSurface surface,
        CancellationToken cancellationToken = default);

    Task RevokeAsync(Guid id, CancellationToken cancellationToken = default);

    // The issued tokens for the admin management view: never the plaintext (only the hash is
    // stored), just the metadata needed to list and revoke.
    Task<IReadOnlyList<ShareTokenSummary>> ListAsync(CancellationToken cancellationToken = default);
}

// One issued token as shown on the admin management surface. Carries no plaintext token and no
// hash — only the label, surface, who created it, when, and whether it is still live.
public sealed record ShareTokenSummary(
    Guid Id,
    string Label,
    ShareSurface Surface,
    string CreatedBy,
    DateTime CreatedAtUtc,
    DateTime? RevokedAtUtc);
