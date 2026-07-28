using System.Net;
using DfE.CheckPerformanceData.Web.Extensions;
using DfE.CheckPerformanceData.Web.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DfE.CheckPerformanceData.IntegrationTests.Web;

// Locks in the "user-quotable session id" surface. Every text/html response — public
// pages, admin pages, 404s (once the app returns text/html for them), 500 error pages —
// must carry an HTML comment `<!-- session: <id> -->` immediately before </body>.
// Users see the id in view-source and quote it back to support. Non-text/html
// responses (JSON, robots.txt, streaming) must NOT get the comment — the injector's
// buffered-body pattern would break SSE and other streams if it fired on them.
public sealed class SessionSourceCommentMiddlewareTests
{
    [Fact]
    public async Task HtmlResponse_ContainsSessionCommentBeforeBodyClose()
    {
        using var host = await BuildHostAsync(responseKind: "html");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("<!-- session: ", body);
        Assert.Contains(" -->", body);

        // Comment must sit immediately before </body>, not appended arbitrarily.
        var commentIndex = body.LastIndexOf("<!-- session: ", StringComparison.Ordinal);
        var bodyCloseIndex = body.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        Assert.True(commentIndex >= 0 && bodyCloseIndex >= 0);
        Assert.True(commentIndex < bodyCloseIndex, "comment should precede </body>");
    }

    [Fact]
    public async Task HtmlResponse_ContainsAStableNonEmptySessionId()
    {
        using var host = await BuildHostAsync(responseKind: "html");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        var start = body.IndexOf("<!-- session: ", StringComparison.Ordinal) + "<!-- session: ".Length;
        var end = body.IndexOf(" -->", start, StringComparison.Ordinal);
        var sessionId = body[start..end];

        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }

    [Fact]
    public async Task SessionId_PersistsAcrossRequestsWithSameCookie()
    {
        using var host = await BuildHostAsync(responseKind: "html");
        var client = host.GetTestClient();

        var first = await client.GetAsync("/");
        var firstBody = await first.Content.ReadAsStringAsync();
        var firstId = ExtractSessionId(firstBody);
        var cookie = ExtractSessionCookie(first);

        var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add("Cookie", cookie);
        var second = await client.SendAsync(req);
        var secondBody = await second.Content.ReadAsStringAsync();
        var secondId = ExtractSessionId(secondBody);

        Assert.False(string.IsNullOrWhiteSpace(firstId));
        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public async Task NonHtmlResponse_DoesNotContainSessionComment()
    {
        using var host = await BuildHostAsync(responseKind: "json");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<!-- session: ", body);
    }

    // Landmine #1 divergence #2: unlike the diagnostic-footer middleware this DOES fire
    // on non-200 responses so error pages and 404 pages users see also carry the id.
    [Fact]
    public async Task Error500HtmlResponse_ContainsSessionComment()
    {
        using var host = await BuildHostAsync(responseKind: "html-500");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("<!-- session: ", body);
    }

    [Fact]
    public async Task NotFound404HtmlResponse_ContainsSessionComment()
    {
        using var host = await BuildHostAsync(responseKind: "html-404");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<!-- session: ", body);
    }

    // Guard against double injection on subsequent middleware passes.
    [Fact]
    public async Task Injection_IsIdempotent_WhenBodyAlreadyContainsSessionComment()
    {
        using var host = await BuildHostAsync(responseKind: "html-pre-stamped");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        var occurrences = 0;
        var idx = 0;
        while ((idx = body.IndexOf("<!-- session: ", idx, StringComparison.Ordinal)) >= 0)
        {
            occurrences++;
            idx += "<!-- session: ".Length;
        }
        Assert.Equal(1, occurrences);
    }

    // The extension-off switch exists for hostile environments where visible session ids
    // are unacceptable; default is ON so support can lean on the id everywhere else.
    [Fact]
    public async Task ShowSessionCommentFalse_SuppressesInjection()
    {
        using var host = await BuildHostAsync(responseKind: "html", showSessionComment: false);
        var client = host.GetTestClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<!-- session: ", body);
    }

    private static async Task<IHost> BuildHostAsync(string responseKind, bool showSessionComment = true)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();

                web.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["SearchAnalytics:ShowSessionComment"] = showSessionComment ? "true" : "false"
                    });
                });

                web.ConfigureServices((ctx, services) =>
                {
                    services.AddDistributedMemoryCache();
                    services.AddCpdSession(ctx.Configuration, ctx.HostingEnvironment);
                });

                web.Configure(app =>
                {
                    app.UseSession();
                    app.UseMiddleware<SessionAbsoluteLifetimeMiddleware>();
                    app.UseMiddleware<SessionSourceCommentMiddleware>();
                    app.Run(async context =>
                    {
                        switch (responseKind)
                        {
                            case "html":
                                context.Response.ContentType = "text/html; charset=utf-8";
                                await context.Response.WriteAsync("<html><body><p>hello</p></body></html>");
                                break;
                            case "html-500":
                                context.Response.ContentType = "text/html; charset=utf-8";
                                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                                await context.Response.WriteAsync("<html><body><p>error</p></body></html>");
                                break;
                            case "html-404":
                                context.Response.ContentType = "text/html; charset=utf-8";
                                context.Response.StatusCode = StatusCodes.Status404NotFound;
                                await context.Response.WriteAsync("<html><body><p>not found</p></body></html>");
                                break;
                            case "html-pre-stamped":
                                context.Response.ContentType = "text/html; charset=utf-8";
                                await context.Response.WriteAsync(
                                    "<html><body><p>hello</p><!-- session: preset --></body></html>");
                                break;
                            case "json":
                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync("{\"ok\":true}");
                                break;
                            default:
                                throw new InvalidOperationException($"Unknown response kind {responseKind}");
                        }
                    });
                });
            })
            .StartAsync();

        return host;
    }

    private static string ExtractSessionId(string body)
    {
        var start = body.IndexOf("<!-- session: ", StringComparison.Ordinal) + "<!-- session: ".Length;
        var end = body.IndexOf(" -->", start, StringComparison.Ordinal);
        return body[start..end];
    }

    private static string ExtractSessionCookie(HttpResponseMessage response)
    {
        Assert.True(response.Headers.Contains("Set-Cookie"));
        foreach (var raw in response.Headers.GetValues("Set-Cookie"))
        {
            if (raw.StartsWith(".AspNetCore.Session=", StringComparison.Ordinal))
            {
                var end = raw.IndexOf(';');
                return end < 0 ? raw : raw[..end];
            }
        }
        throw new Xunit.Sdk.XunitException("No .AspNetCore.Session cookie in response.");
    }
}
