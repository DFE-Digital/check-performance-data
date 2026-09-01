using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Common;

/// <summary>
/// Scopes a CMS content key to a checking window type, so a block shared by every key stage can
/// say "pupil" on KS4 and "student" on 16-19.
/// </summary>
/// <remarks>
/// A content block seeds its default text once per key and never again (see
/// <c>IContentBlockService.EnsureAsync</c>), so one key can only ever hold one noun. Suffixing the
/// key gives each window type its own block, seeded with its own wording and editable on its own.
/// The unsuffixed keys are left orphaned rather than deleted — that is the documented way to change
/// already-seeded content, and it keeps any hand-edited prose recoverable.
/// </remarks>
public static class WindowScopedContentKey
{
    public static string For(string key, CheckingWindowType windowType) =>
        $"{key}-{windowType.ToString().ToLowerInvariant()}";
}
