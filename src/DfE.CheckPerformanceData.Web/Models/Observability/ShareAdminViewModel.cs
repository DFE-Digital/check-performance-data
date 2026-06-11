using DfE.CheckPerformanceData.Application.Observability;

namespace DfE.CheckPerformanceData.Web.Models.Observability;

// The admin token-management view: the list of issued tokens (metadata only, never the plaintext
// or the hash) plus, immediately after a generation, the one-time plaintext token to copy. The
// plaintext is carried only for the single post-redirect render and is never stored.
public sealed class ShareAdminViewModel
{
    public required IReadOnlyList<ShareTokenSummary> Tokens { get; init; }

    // The just-generated plaintext token, shown once. Null on a normal page load.
    public string? NewToken { get; init; }
    public string? NewTokenSurface { get; init; }
}
