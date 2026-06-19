using System.Text;
using DfE.CheckPerformanceData.Web.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.Web.Diagnostics;

// Injects an HTML comment block immediately before </body> with the current
// environment, auth scheme, and full claim set so view-source on the dev server
// answers the "what does this environment call itself?" question without needing a
// debugger. NEVER renders in Production: env.IsProduction() short-circuits the
// middleware regardless of config. In any non-prod env it defaults to ON; set
// Diagnostics:ShowSessionFooter=false in config (env var
// `Diagnostics__ShowSessionFooter=false`) to explicitly disable.
public sealed class DiagnosticFooterMiddleware(
    RequestDelegate next,
    IHostEnvironment env,
    IConfiguration config)
{
    private const string ConfigKey = "Diagnostics:ShowSessionFooter";
    private const string BodyClose = "</body>";

    private readonly bool _enabled = !env.IsProduction()
        && (config.GetValue<bool?>(ConfigKey) ?? true);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled || !ShouldIntercept(context))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);

            if (!IsHtmlResponse(context))
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody);
                return;
            }

            buffer.Position = 0;
            var html = await new StreamReader(buffer).ReadToEndAsync();

            var comment = BuildCommentBlock(context, env);
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

    // Skip non-GET requests, and any request negotiating a server-sent-event stream: an SSE
    // response stays open for the lifetime of the page, so swapping in a MemoryStream would
    // hold every event in memory and the client would never receive a byte.
    private static bool ShouldIntercept(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && !context.Request.Headers.Accept.ToString()
            .Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);

    private static bool IsHtmlResponse(HttpContext context) =>
        context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true
        && context.Response.StatusCode == StatusCodes.Status200OK;

    private static string InjectBeforeBodyClose(string html, string comment)
    {
        var index = html.LastIndexOf(BodyClose, StringComparison.OrdinalIgnoreCase);
        return index < 0
            ? html + comment   // no </body>: append to end so it's still visible in view-source
            : html.Insert(index, comment);
    }

    private static string BuildCommentBlock(HttpContext context, IHostEnvironment env)
    {
        var user = context.User;
        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine("<!--");
        sb.AppendLine("  ============================================================");
        sb.AppendLine("  CPD DEV DIAGNOSTIC — TODO remove before this view ships beyond non-prod");
        sb.AppendLine("  ============================================================");
        sb.AppendLine($"  ASPNETCORE_ENVIRONMENT : {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(unset)"}");
        sb.AppendLine($"  Hosting environment    : {env.EnvironmentName}");
        sb.AppendLine($"  Authenticated          : {user.Identity?.IsAuthenticated ?? false}");
        sb.AppendLine($"  Auth scheme            : {user.Identity?.AuthenticationType ?? "(none)"}");

        if (context.Request.Cookies.TryGetValue(DevImpersonationConstants.CookieName, out var marker))
        {
            sb.AppendLine($"  Impersonation cookie   : {SanitiseForComment(marker)}");
        }
        else
        {
            sb.AppendLine("  Impersonation cookie   : (absent)");
        }

        sb.AppendLine("  ------------------------------------------------------------");
        sb.AppendLine("  Roles:");
        var roles = user.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();
        if (roles.Count == 0)
        {
            sb.AppendLine("    (none)");
        }
        else
        {
            foreach (var role in roles)
            {
                sb.AppendLine($"    - {SanitiseForComment(role)}");
            }
        }
        sb.AppendLine("  ------------------------------------------------------------");
        sb.AppendLine("  All claims:");
        foreach (var claim in user.Claims)
        {
            sb.AppendLine($"    {SanitiseForComment(claim.Type)} : {SanitiseForComment(claim.Value)}");
        }
        sb.AppendLine("  ============================================================");
        sb.AppendLine("-->");

        return sb.ToString();
    }

    // HTML comments terminate on '-->'. Any '--' inside a claim value would either close
    // the comment early or invalidate it. Escape to '- -' so the block stays well-formed.
    private static string SanitiseForComment(string value) =>
        value.Replace("--", "- -").Replace("\r", " ").Replace("\n", " ");
}
