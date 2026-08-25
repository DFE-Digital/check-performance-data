namespace DfE.CheckPerformanceData.Web.Extensions;

// Session-identifier masking for admin analytics surfaces. First 8 chars + "…" is the
// house convention; drill-in `title` attributes typically expose the full value on
// hover so an admin can still copy the underlying ID when it matters.
//
// Shared here so a masking-rule change (character count, ellipsis, tokenisation via a
// PII-reveal service) lands in one place instead of drifting across every drill-in view.
public static class SessionIdMasking
{
    private const int MaskedPrefixLength = 8;
    private const string UnknownLabel = "(unknown)";
    private const string TruncationSuffix = "…";

    public static string MaskSessionId(this string? sessionId) =>
        string.IsNullOrEmpty(sessionId)
            ? UnknownLabel
            : (sessionId.Length <= MaskedPrefixLength
                ? sessionId
                : sessionId[..MaskedPrefixLength] + TruncationSuffix);
}
