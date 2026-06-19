namespace DfE.CheckPerformance.Persistence.Entities;

// An issued share/wallboard access token. The plaintext token is shown once on generation and
// never stored — only its SHA-256 hash lives here, so a database read cannot reveal a live token.
// One row per issued token; revocation sets RevokedAtUtc and a revoked row stops validating
// immediately. Surface scopes the token to exactly one anonymised surface (the share link or the
// wallboard) so a token cannot be replayed against the other.
public sealed class ShareToken
{
    public Guid Id { get; set; }

    // The SHA-256 hash of the opaque token. The token itself is never persisted.
    public string TokenHash { get; set; } = string.Empty;

    // A human label so an admin can tell tokens apart when revoking (e.g. "Ofsted demo").
    public string Label { get; set; } = string.Empty;

    // The surface this token grants access to, stored as the enum name ("Share" / "Wallboard").
    public string Surface { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    // Null while the token is live; set when revoked.
    public DateTime? RevokedAtUtc { get; set; }
}
