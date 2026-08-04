using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace DfE.CheckPerformanceData.Web.Middleware;

// Injects an HTML comment carrying the current session id immediately before </body>
// on every text/html response — public pages, admin pages, 404s, 500 error pages.
// Users see the id in view-source and quote it back to support.
//
// Two deliberate divergences from DiagnosticFooterMiddleware:
//   1. No hosting-environment guard — the id must be quotable in Production too,
//      that's the whole point of the surface.
//   2. No 200-only guard — 404 and 500 responses need the comment too, because
//      those are exactly the pages users complain about.
//
// The SSE (text/event-stream) short-circuit IS preserved — buffering an infinite
// stream to inject a body-close comment would break the stream and hold memory
// forever. The session-cookie commit that SessionAbsoluteLifetimeMiddleware performs
// (via SetString on first access) is what makes Session.Id stable here; without that
// side effect, reading Session.Id would return a fresh id on every request because
// the framework lazy-writes the cookie on first store mutation.
public sealed class SessionSourceCommentMiddleware
{
    private const string ConfigKey = "SearchAnalytics:ShowSessionComment";
    private const string BodyClose = "</body>";
    private const string CommentPrefix = "<!-- session: ";

    private readonly RequestDelegate _next;
    private readonly bool _enabled;

    public SessionSourceCommentMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _enabled = configuration.GetValue<bool?>(ConfigKey) ?? true;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled || !ShouldIntercept(context))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            if (!IsHtmlResponse(context))
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody);
                return;
            }

            buffer.Position = 0;
            var html = await new StreamReader(buffer).ReadToEndAsync();

            // Idempotent — a second middleware pass on the same buffer must not double-
            // inject. The comment is unique enough that a substring match is safe.
            if (html.Contains(CommentPrefix, StringComparison.Ordinal))
            {
                var passthrough = Encoding.UTF8.GetBytes(html);
                context.Response.ContentLength = passthrough.Length;
                await originalBody.WriteAsync(passthrough);
                return;
            }

            var sessionId = context.Session?.Id ?? string.Empty;
            var comment = $"{CommentPrefix}{sessionId} -->";
            var injected = InjectBeforeBodyClose(html, comment);
            var bytes = Encoding.UTF8.GetBytes(injected);

            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool ShouldIntercept(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && !context.Request.Headers.Accept.ToString()
            .Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);

    private static bool IsHtmlResponse(HttpContext context) =>
        context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;

    private static string InjectBeforeBodyClose(string html, string comment)
    {
        var index = html.LastIndexOf(BodyClose, StringComparison.OrdinalIgnoreCase);
        return index < 0
            ? html + comment
            : html.Insert(index, comment);
    }
}
