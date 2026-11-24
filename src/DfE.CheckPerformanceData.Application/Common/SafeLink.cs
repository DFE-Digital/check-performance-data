namespace DfE.CheckPerformanceData.Application.Common;

/// <summary>
/// Decides whether a link that an editor typed is safe to put in an <c>href</c>. A safe link is a
/// web address (http or https, with a host), a path on this site that starts with one "/", or a
/// "#" link to a place on the page. Every other scheme (javascript:, data:, mailto: and so on) is
/// refused, and so are "//host" and "/\host", which browsers read as a link to another site.
/// White space and control characters are refused, because browsers remove some of them before
/// they read the scheme.
/// </summary>
public static class SafeLink
{
    public static bool IsSafe(string? href)
    {
        if (string.IsNullOrEmpty(href) || href.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return false;

        if (href[0] == '#') return true;

        if (href[0] == '/')
            return href.Length == 1 || (href[1] != '/' && href[1] != '\\');

        // The host must follow "://" at once: .NET reads "https:///host" as having a host.
        var hostStart = href.IndexOf("://", StringComparison.Ordinal) + 3;
        if (hostStart < 3 || hostStart >= href.Length || href[hostStart] == '/') return false;

        return Uri.TryCreate(href, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host)
            && !href.Contains('\\');
    }
}
