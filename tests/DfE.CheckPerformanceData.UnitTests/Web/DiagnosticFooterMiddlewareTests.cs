using System.Text;
using DfE.CheckPerformanceData.Web.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// The dev diagnostic footer buffers GET responses so it can inject a comment before
// </body>. Buffering must never apply to server-sent-event requests: an SSE response
// stays open for the lifetime of the page, so a buffered body would hold every event
// in memory and the client would never receive a byte.
public sealed class DiagnosticFooterMiddlewareTests
{
    [Fact]
    public async Task EventStreamRequest_PassesTheResponseBodyThroughUnbuffered()
    {
        Stream? bodySeenByNext = null;
        var middleware = new DiagnosticFooterMiddleware(
            async ctx =>
            {
                bodySeenByNext = ctx.Response.Body;
                ctx.Response.ContentType = "text/event-stream";
                await ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("event: snapshot\ndata: {}\n\n"));
            },
            DevelopmentEnvironment(),
            EnabledConfiguration());

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Accept = "text/event-stream";
        var original = new MemoryStream();
        context.Response.Body = original;

        await middleware.InvokeAsync(context);

        // The handler must write straight to the client stream — each event has to flush
        // as it is produced, which a MemoryStream swap makes impossible.
        Assert.Same(original, bodySeenByNext);
        Assert.True(original.Length > 0);
    }

    [Fact]
    public async Task HtmlPageRequest_IsBufferedAndGetsTheDiagnosticComment()
    {
        var middleware = new DiagnosticFooterMiddleware(
            async ctx =>
            {
                ctx.Response.ContentType = "text/html";
                await ctx.Response.WriteAsync("<html><body>hello</body></html>");
            },
            DevelopmentEnvironment(),
            EnabledConfiguration());

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        var original = new MemoryStream();
        context.Response.Body = original;

        await middleware.InvokeAsync(context);

        var html = Encoding.UTF8.GetString(original.ToArray());
        Assert.Contains("CPD DEV DIAGNOSTIC", html);
        Assert.Contains("</body>", html);
    }

    [Fact]
    public async Task NonHtmlGet_IsCopiedBackUntouched()
    {
        var middleware = new DiagnosticFooterMiddleware(
            async ctx =>
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"ok\":true}");
            },
            DevelopmentEnvironment(),
            EnabledConfiguration());

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        var original = new MemoryStream();
        context.Response.Body = original;

        await middleware.InvokeAsync(context);

        Assert.Equal("{\"ok\":true}", Encoding.UTF8.GetString(original.ToArray()));
    }

    private static IHostEnvironment DevelopmentEnvironment()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Development);
        return env;
    }

    private static IConfiguration EnabledConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Diagnostics:ShowSessionFooter"] = "true",
            })
            .Build();
}
